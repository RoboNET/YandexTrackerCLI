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
