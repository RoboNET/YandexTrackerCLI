using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Auth.Federated;

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using TUnit.Core;
using YandexTrackerCLI.Auth.Federated;
using Http;
using Core.Api.Errors;
using YandexTrackerCLI.Core.Auth;

/// <summary>
/// Юнит-тесты <see cref="FederatedTokenProvider"/>: кеш-хит, обновление через fake
/// refresh client, и обработка 401 + <c>DPoP-Nonce</c> retry.
/// </summary>
public sealed class FederatedTokenProviderTests
{
    private sealed class FakeRefresh : IFederatedRefreshClient
    {
        private readonly Func<FederatedTokenResult> _impl;
        public int CallCount { get; private set; }

        public FakeRefresh(Func<FederatedTokenResult> impl) => _impl = impl;

        public Task<FederatedTokenResult> Refresh(string refreshToken, string clientId, ECDsa key, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_impl());
        }
    }

    [Test]
    public async Task CachedAccessToken_HitsCache_NoRefresh()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cache = new TokenCache(Path.Combine(Path.GetTempPath(), "yt-fed-" + Guid.NewGuid() + ".json"));
        var fake = new FakeRefresh(() => new FederatedTokenResult("iam-fresh", "rt", DateTimeOffset.UtcNow.AddHours(1)));

        // Pre-seed the cache with a still-valid entry.
        await cache.SetAsync("ci:federated:fed-1", "iam-cached", DateTimeOffset.UtcNow.AddMinutes(30), CancellationToken.None);

        using var provider = new FederatedTokenProvider(
            "ci:federated:fed-1",
            key,
            cache,
            fake,
            refreshToken: "rt-original",
            clientId: "yc.oauth.public-sdk");

        var h = await provider.GetAuthorizationAsync(CancellationToken.None);

        await Assert.That(h.Scheme).IsEqualTo("Bearer");
        await Assert.That(h.Parameter).IsEqualTo("iam-cached");
        await Assert.That(fake.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Expired_RefreshesAndCaches()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cache = new TokenCache(Path.Combine(Path.GetTempPath(), "yt-fed-" + Guid.NewGuid() + ".json"));
        var fake = new FakeRefresh(() => new FederatedTokenResult("iam-refreshed", "rt-new", DateTimeOffset.UtcNow.AddHours(1)));

        using var provider = new FederatedTokenProvider(
            "ci:federated:fed-1",
            key,
            cache,
            fake,
            refreshToken: "rt-original",
            clientId: "yc.oauth.public-sdk");

        var h1 = await provider.GetAuthorizationAsync(CancellationToken.None);
        var h2 = await provider.GetAuthorizationAsync(CancellationToken.None);

        await Assert.That(h1.Parameter).IsEqualTo("iam-refreshed");
        await Assert.That(h2.Parameter).IsEqualTo("iam-refreshed");
        // First call refreshes; second call hits the cache.
        await Assert.That(fake.CallCount).IsEqualTo(1);
    }

    /// <summary>
    /// Refresh-клиент, который записывает предъявленные ему refresh-токены и отдаёт
    /// заранее заданные ответы по одному на вызов.
    /// </summary>
    private sealed class ScriptedRefresh : IFederatedRefreshClient
    {
        private readonly Queue<FederatedTokenResult> _results;

        public ScriptedRefresh(params FederatedTokenResult[] results) => _results = new Queue<FederatedTokenResult>(results);

        public List<string> SeenRefreshTokens { get; } = new();

        public Task<FederatedTokenResult> Refresh(string refreshToken, string clientId, ECDsa key, CancellationToken ct)
        {
            SeenRefreshTokens.Add(refreshToken);
            return Task.FromResult(_results.Dequeue());
        }
    }

    /// <summary>
    /// Сток, записывающий все сохранённые refresh-токены; опционально падает при сохранении.
    /// </summary>
    private sealed class RecordingSink : IRefreshTokenSink
    {
        private readonly bool _throws;

        public RecordingSink(bool throws = false) => _throws = throws;

        public List<string> Saved { get; } = new();

        public Task SaveRefreshToken(string refreshToken, CancellationToken ct)
        {
            Saved.Add(refreshToken);
            return _throws
                ? Task.FromException(new IOException("config is not writable"))
                : Task.CompletedTask;
        }
    }

    /// <summary>
    /// Сервер провернул refresh-токен: новый уходит в сток и используется следующим
    /// обновлением в этом же процессе, а не исходный (тот уже мёртв на стороне сервера).
    /// </summary>
    [Test]
    public async Task RotatedRefreshToken_IsSaved_AndUsedByNextRefresh()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cache = new TokenCache(Path.Combine(Path.GetTempPath(), "yt-fed-" + Guid.NewGuid() + ".json"));

        // Первый access-токен истекает сразу (внутри 60-секундного leeway кеша),
        // поэтому второй запрос авторизации снова идёт на refresh.
        var refresh = new ScriptedRefresh(
            new FederatedTokenResult("iam-1", "rt-rotated-1", DateTimeOffset.UtcNow),
            new FederatedTokenResult("iam-2", "rt-rotated-2", DateTimeOffset.UtcNow.AddHours(1)));
        var sink = new RecordingSink();

        using var provider = new FederatedTokenProvider(
            "ci:federated:fed-1",
            key,
            cache,
            refresh,
            refreshToken: "rt-original",
            clientId: "yc.oauth.public-sdk",
            refreshTokenSink: sink);

        var h1 = await provider.GetAuthorizationAsync(CancellationToken.None);
        var h2 = await provider.GetAuthorizationAsync(CancellationToken.None);

        await Assert.That(h1.Parameter).IsEqualTo("iam-1");
        await Assert.That(h2.Parameter).IsEqualTo("iam-2");
        await Assert.That(refresh.SeenRefreshTokens).IsEquivalentTo(new[] { "rt-original", "rt-rotated-1" });
        await Assert.That(sink.Saved).IsEquivalentTo(new[] { "rt-rotated-1", "rt-rotated-2" });
    }

    /// <summary>
    /// Тот же refresh-токен в ответе — сохранять нечего: запись на горячем пути не делается.
    /// </summary>
    [Test]
    public async Task UnchangedRefreshToken_IsNotSaved()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cache = new TokenCache(Path.Combine(Path.GetTempPath(), "yt-fed-" + Guid.NewGuid() + ".json"));
        var refresh = new ScriptedRefresh(
            new FederatedTokenResult("iam-1", "rt-original", DateTimeOffset.UtcNow.AddHours(1)));
        var sink = new RecordingSink();

        using var provider = new FederatedTokenProvider(
            "ci:federated:fed-1",
            key,
            cache,
            refresh,
            refreshToken: "rt-original",
            clientId: "yc.oauth.public-sdk",
            refreshTokenSink: sink);

        var h = await provider.GetAuthorizationAsync(CancellationToken.None);

        await Assert.That(h.Parameter).IsEqualTo("iam-1");
        await Assert.That(sink.Saved.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Сервер не вернул refresh_token вовсе (нерotирующая федерация) — тоже без записи,
    /// и в памяти остаётся исходный токен.
    /// </summary>
    [Test]
    public async Task MissingRefreshTokenInResponse_KeepsOriginal_AndSavesNothing()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cache = new TokenCache(Path.Combine(Path.GetTempPath(), "yt-fed-" + Guid.NewGuid() + ".json"));
        var refresh = new ScriptedRefresh(
            new FederatedTokenResult("iam-1", null, DateTimeOffset.UtcNow),
            new FederatedTokenResult("iam-2", null, DateTimeOffset.UtcNow.AddHours(1)));
        var sink = new RecordingSink();

        using var provider = new FederatedTokenProvider(
            "ci:federated:fed-1",
            key,
            cache,
            refresh,
            refreshToken: "rt-original",
            clientId: "yc.oauth.public-sdk",
            refreshTokenSink: sink);

        await provider.GetAuthorizationAsync(CancellationToken.None);
        await provider.GetAuthorizationAsync(CancellationToken.None);

        await Assert.That(refresh.SeenRefreshTokens).IsEquivalentTo(new[] { "rt-original", "rt-original" });
        await Assert.That(sink.Saved.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Отказ сохранения — fail-soft: access-токен на руках валиден, авторизация выдаётся,
    /// а провёрнутый токен всё равно используется дальше в этом процессе.
    /// </summary>
    [Test]
    public async Task SinkFailure_DoesNotFailAuthorization()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cache = new TokenCache(Path.Combine(Path.GetTempPath(), "yt-fed-" + Guid.NewGuid() + ".json"));
        var refresh = new ScriptedRefresh(
            new FederatedTokenResult("iam-1", "rt-rotated", DateTimeOffset.UtcNow),
            new FederatedTokenResult("iam-2", "rt-rotated", DateTimeOffset.UtcNow.AddHours(1)));
        var sink = new RecordingSink(throws: true);

        using var provider = new FederatedTokenProvider(
            "ci:federated:fed-1",
            key,
            cache,
            refresh,
            refreshToken: "rt-original",
            clientId: "yc.oauth.public-sdk",
            refreshTokenSink: sink);

        var h1 = await provider.GetAuthorizationAsync(CancellationToken.None);
        var h2 = await provider.GetAuthorizationAsync(CancellationToken.None);

        await Assert.That(h1.Parameter).IsEqualTo("iam-1");
        await Assert.That(h2.Parameter).IsEqualTo("iam-2");
        await Assert.That(refresh.SeenRefreshTokens).IsEquivalentTo(new[] { "rt-original", "rt-rotated" });
    }

    private sealed class FailingRefresh : IFederatedRefreshClient
    {
        public int CallCount { get; private set; }

        public Task<FederatedTokenResult> Refresh(string refreshToken, string clientId, ECDsa key, CancellationToken ct)
        {
            CallCount++;
            throw new TrackerException(ErrorCode.AuthFailed, "refresh_token expired", httpStatus: 400);
        }
    }

    private sealed class FakeReloginHandler : IFederatedReloginHandler
    {
        private readonly FederatedTokenResult _result;
        public int CallCount { get; private set; }

        public FakeReloginHandler(FederatedTokenResult result) => _result = result;

        public Task<FederatedTokenResult> ReloginAsync(CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    [Test]
    public async Task RefreshFails_WithReloginHandler_InvokesReloginAndServesNewToken()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cache = new TokenCache(Path.Combine(Path.GetTempPath(), "yt-fed-" + Guid.NewGuid() + ".json"));
        var refresh = new FailingRefresh();
        var relogin = new FakeReloginHandler(
            new FederatedTokenResult("iam-after-relogin", "rt-fresh", DateTimeOffset.UtcNow.AddHours(1)));
        var sink = new RecordingSink();

        using var provider = new FederatedTokenProvider(
            "ci:federated:fed-1",
            key,
            cache,
            refresh,
            refreshToken: "rt-expired",
            clientId: "yc.oauth.public-sdk",
            relogin: relogin,
            reloginCommandHint: "yt auth relogin --profile ci",
            refreshTokenSink: sink);

        var h = await provider.GetAuthorizationAsync(CancellationToken.None);

        await Assert.That(h.Scheme).IsEqualTo("Bearer");
        await Assert.That(h.Parameter).IsEqualTo("iam-after-relogin");
        await Assert.That(refresh.CallCount).IsEqualTo(1);
        await Assert.That(relogin.CallCount).IsEqualTo(1);

        // Ветка перелогина сохраняет профиль сама (FederatedReloginService), поэтому сток
        // здесь не дёргается — иначе один и тот же токен писался бы дважды.
        await Assert.That(sink.Saved.Count).IsEqualTo(0);

        // Second call hits the cache populated by the relogin result — no extra relogin.
        var h2 = await provider.GetAuthorizationAsync(CancellationToken.None);
        await Assert.That(h2.Parameter).IsEqualTo("iam-after-relogin");
        await Assert.That(relogin.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task RefreshFails_NoReloginHandler_ThrowsAuthFailedWithCommandHint()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cache = new TokenCache(Path.Combine(Path.GetTempPath(), "yt-fed-" + Guid.NewGuid() + ".json"));
        var refresh = new FailingRefresh();

        using var provider = new FederatedTokenProvider(
            "ci:federated:fed-1",
            key,
            cache,
            refresh,
            refreshToken: "rt-expired",
            clientId: "yc.oauth.public-sdk",
            relogin: null,
            reloginCommandHint: "yt auth relogin --profile ci");

        var ex = await Assert.ThrowsAsync<TrackerException>(
            () => provider.GetAuthorizationAsync(CancellationToken.None));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.AuthFailed);
        await Assert.That(ex.Message).Contains("yt auth relogin --profile ci");
        await Assert.That(ex.ReloginCommand).IsEqualTo("yt auth relogin --profile ci");
        await Assert.That(ex.HttpStatus).IsEqualTo(400);
    }

    [Test]
    public async Task CachedToken_NeitherRefreshNorReloginInvoked()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cache = new TokenCache(Path.Combine(Path.GetTempPath(), "yt-fed-" + Guid.NewGuid() + ".json"));
        var refresh = new FailingRefresh();
        var relogin = new FakeReloginHandler(
            new FederatedTokenResult("should-not-be-used", "rt", DateTimeOffset.UtcNow.AddHours(1)));

        await cache.SetAsync("ci:federated:fed-1", "iam-cached", DateTimeOffset.UtcNow.AddMinutes(30), CancellationToken.None);

        using var provider = new FederatedTokenProvider(
            "ci:federated:fed-1",
            key,
            cache,
            refresh,
            refreshToken: "rt-original",
            clientId: "yc.oauth.public-sdk",
            relogin: relogin,
            reloginCommandHint: "yt auth relogin --profile ci");

        var h = await provider.GetAuthorizationAsync(CancellationToken.None);

        await Assert.That(h.Parameter).IsEqualTo("iam-cached");
        await Assert.That(refresh.CallCount).IsEqualTo(0);
        await Assert.That(relogin.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Refresh_401WithNonce_RetriesAndSucceeds()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var handler = new TestHttpMessageHandler();
        handler.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            r.Headers.TryAddWithoutValidation("DPoP-Nonce", "server-nonce-42");
            return r;
        });
        handler.Push(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"iam-after-nonce","refresh_token":"rt-new","expires_in":3600}""",
                Encoding.UTF8,
                "application/json"),
        });

        using var http = new HttpClient(handler);
        var client = new FederatedRefreshClient(http, "https://token.example.test/oauth/token");

        var result = await client.Refresh("rt", "yc.oauth.public-sdk", key, CancellationToken.None);

        await Assert.That(result.AccessToken).IsEqualTo("iam-after-nonce");
        await Assert.That(handler.Seen.Count).IsEqualTo(2);

        // First request: DPoP proof without nonce claim.
        var firstProof = handler.Seen[0].Headers.GetValues("DPoP").Single();
        await Assert.That(DecodePayload(firstProof).Contains("\"nonce\"")).IsFalse();

        // Second request: DPoP proof includes the server-supplied nonce.
        var secondProof = handler.Seen[1].Headers.GetValues("DPoP").Single();
        await Assert.That(DecodePayload(secondProof).Contains("server-nonce-42")).IsTrue();
    }

    [Test]
    public async Task Refresh_Non401Failure_Throws()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var handler = new TestHttpMessageHandler()
            .Push(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"invalid_grant\"}"),
            });

        using var http = new HttpClient(handler);
        var client = new FederatedRefreshClient(http, "https://token.example.test/oauth/token");

        var ex = await Assert.ThrowsAsync<TrackerException>(
            () => client.Refresh("rt", "yc.oauth.public-sdk", key, CancellationToken.None));
        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.AuthFailed);
        await Assert.That(ex.HttpStatus).IsEqualTo(400);
    }

    private static string DecodePayload(string jwt)
    {
        var seg = jwt.Split('.')[1];
        var padded = seg.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
