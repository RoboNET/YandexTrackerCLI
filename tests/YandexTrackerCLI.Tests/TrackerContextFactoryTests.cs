using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests;

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using TUnit.Core;
using Http;
using Core.Api.Errors;
using YandexTrackerCLI.Core.Auth;

/// <summary>
/// End-to-end тесты <see cref="TrackerContextFactory"/>: резолв профиля из конфига/env
/// и сборка полной HTTP-пайплайны (auth + org + read-only).
/// Мутируют глобальное состояние (env + Console), поэтому исполняются последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class TrackerContextFactoryTests
{
    /// <summary>
    /// OAuth-профиль: Authorization заголовок <c>OAuth &lt;token&gt;</c> и <c>X-Cloud-Org-ID</c>.
    /// </summary>
    [Test]
    public async Task OAuthProfile_BuildsContext_ThatSendsCorrectHeaders()
    {
        using var env = new TestEnv();
        env.Set("YT_API_BASE_URL", null);
        env.Set("YT_TIMEOUT", null);
        env.SetConfig("""
        {"default_profile":"a","profiles":{"a":{"org_type":"cloud","org_id":"o1",
          "read_only":false,"auth":{"type":"oauth","token":"y0_X"}}}}
        """);
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));

        using var ctx = await TrackerContextFactory.CreateAsync(
            profileName: null,
            cliReadOnly: false,
            timeoutSeconds: null,
            innerHandler: inner);

        _ = await ctx.RawHttp.GetAsync("https://api.tracker.yandex.net/v3/myself");
        var req = inner.Seen[0];
        await Assert.That(req.Headers.Authorization!.Scheme).IsEqualTo("OAuth");
        await Assert.That(req.Headers.Authorization.Parameter).IsEqualTo("y0_X");
        await Assert.That(req.Headers.GetValues("X-Cloud-Org-ID").Single()).IsEqualTo("o1");
    }

    /// <summary>
    /// IAM-static-профиль: Authorization заголовок <c>Bearer &lt;token&gt;</c>.
    /// </summary>
    [Test]
    public async Task IamStaticProfile_UsesBearerScheme()
    {
        using var env = new TestEnv();
        env.Set("YT_API_BASE_URL", null);
        env.Set("YT_TIMEOUT", null);
        env.SetConfig("""
        {"default_profile":"ci","profiles":{"ci":{"org_type":"cloud","org_id":"o",
          "read_only":false,"auth":{"type":"iam-static","token":"t1.XX"}}}}
        """);
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var ctx = await TrackerContextFactory.CreateAsync(null, false, null, innerHandler: inner);

        _ = await ctx.RawHttp.GetAsync("https://api.tracker.yandex.net/v3/myself");
        var req = inner.Seen[0];
        await Assert.That(req.Headers.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(req.Headers.Authorization.Parameter).IsEqualTo("t1.XX");
    }

    /// <summary>
    /// Service-account-профиль с inline PEM: вставляет <c>iamExchangeOverride</c>
    /// и проверяет, что возвращённый IAM-токен уходит как <c>Bearer</c>.
    /// </summary>
    [Test]
    public async Task ServiceAccountProfile_WithInlinePem_UsesExchangeOverride()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();

        using var env = new TestEnv();
        env.Set("YT_API_BASE_URL", null);
        env.Set("YT_TIMEOUT", null);
        var pemJson = JsonSerializer.Serialize(pem);
        env.SetConfig(
            "{\"default_profile\":\"sa\",\"profiles\":{\"sa\":{\"org_type\":\"cloud\",\"org_id\":\"o\","
            + "\"read_only\":false,\"auth\":{\"type\":\"service-account\",\"service_account_id\":\"sa-1\","
            + "\"key_id\":\"k-1\",\"private_key_pem\":" + pemJson + "}}}}");

        var fakeExchange = new FakeExchange(
            _ => new IamExchangeResult("iam-minted", DateTimeOffset.UtcNow.AddHours(1)));
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));

        using var ctx = await TrackerContextFactory.CreateAsync(
            null, false, null, innerHandler: inner, iamExchangeOverride: fakeExchange);

        _ = await ctx.RawHttp.GetAsync("https://api.tracker.yandex.net/v3/myself");
        var req = inner.Seen[0];
        await Assert.That(req.Headers.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(req.Headers.Authorization.Parameter).IsEqualTo("iam-minted");
    }

    /// <summary>
    /// Read-only профиль: мутирующие запросы блокируются <see cref="ErrorCode.ReadOnlyMode"/>.
    /// </summary>
    [Test]
    public async Task ReadOnlyProfile_BlocksPost()
    {
        using var env = new TestEnv();
        env.Set("YT_API_BASE_URL", null);
        env.Set("YT_TIMEOUT", null);
        env.SetConfig("""
        {"default_profile":"ro","profiles":{"ro":{"org_type":"cloud","org_id":"o",
          "read_only":true,"auth":{"type":"oauth","token":"y0_x"}}}}
        """);
        var inner = new TestHttpMessageHandler();
        using var ctx = await TrackerContextFactory.CreateAsync(null, false, null, innerHandler: inner);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.tracker.yandex.net/v3/issues");
        var ex = await Assert.ThrowsAsync<TrackerException>(() => ctx.RawHttp.SendAsync(req));
        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.ReadOnlyMode);
    }

    /// <summary>
    /// <c>YT_API_BASE_URL</c> переопределяет базовый адрес <see cref="HttpClient"/>.
    /// </summary>
    [Test]
    public async Task EnvBaseUrl_OverridesDefault()
    {
        using var env = new TestEnv();
        env.Set("YT_TIMEOUT", null);
        env.SetConfig("""{"default_profile":"a","profiles":{"a":{"org_type":"cloud","org_id":"o","read_only":false,"auth":{"type":"oauth","token":"y0_X"}}}}""");
        env.Set("YT_API_BASE_URL", "https://example.com/v3/");
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var ctx = await TrackerContextFactory.CreateAsync(null, false, null, innerHandler: inner);
        await Assert.That(ctx.RawHttp.BaseAddress!.ToString()).IsEqualTo("https://example.com/v3/");
    }

    /// <summary>
    /// Federated profile without <c>refresh_token</c> but with a still-valid access token
    /// (expires in the future): falls back to <see cref="IamStaticProvider"/>.
    /// </summary>
    [Test]
    public async Task FederatedProfile_WithoutRefresh_LiveAccess_UsesBearer()
    {
        using var env = new TestEnv();
        env.Set("YT_API_BASE_URL", null);
        env.Set("YT_TIMEOUT", null);
        var futureIso = DateTimeOffset.UtcNow.AddHours(11).ToString("O");
        env.SetConfig(
            "{\"default_profile\":\"fed\",\"profiles\":{\"fed\":{\"org_type\":\"cloud\",\"org_id\":\"o\","
            + "\"read_only\":false,\"auth\":{\"type\":\"federated\",\"token\":\"t1.access12h\","
            + "\"federation_id\":\"fed-1\",\"access_token_expires_at\":\"" + futureIso + "\"}}}}");
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));

        using var ctx = await TrackerContextFactory.CreateAsync(
            profileName: null,
            cliReadOnly: false,
            timeoutSeconds: null,
            innerHandler: inner);

        _ = await ctx.RawHttp.GetAsync("https://api.tracker.yandex.net/v3/myself");
        var req = inner.Seen[0];
        await Assert.That(req.Headers.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(req.Headers.Authorization.Parameter).IsEqualTo("t1.access12h");
    }

    /// <summary>
    /// Federated profile without refresh and with an EXPIRED access token: surfaces
    /// <see cref="ErrorCode.AuthFailed"/> (exit 4) with an actionable re-login command
    /// containing the profile name. No browser is launched (CLI must remain script-friendly).
    /// </summary>
    [Test]
    public async Task FederatedProfile_WithoutRefresh_ExpiredAccess_AuthFailed_WithReLoginInstruction()
    {
        using var env = new TestEnv();
        env.Set("YT_API_BASE_URL", null);
        env.Set("YT_TIMEOUT", null);
        var pastIso = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O");
        env.SetConfig(
            "{\"default_profile\":\"fed-expired\",\"profiles\":{\"fed-expired\":{\"org_type\":\"cloud\",\"org_id\":\"o\","
            + "\"read_only\":false,\"auth\":{\"type\":\"federated\",\"token\":\"t1.expired\","
            + "\"federation_id\":\"fed-1\",\"access_token_expires_at\":\"" + pastIso + "\"}}}}");

        var ex = await Assert.ThrowsAsync<TrackerException>(
            () => TrackerContextFactory.CreateAsync(null, false, null));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.AuthFailed);
        await Assert.That(ex.Code.ToExitCode()).IsEqualTo(4);
        await Assert.That(ex.Message).Contains("yt auth login");
        await Assert.That(ex.Message).Contains("--type federated");
        await Assert.That(ex.Message).Contains("--profile fed-expired");
    }

    /// <summary>
    /// Legacy federated profile (no <c>access_token_expires_at</c> field) with an access
    /// token but no refresh: must still work (we trust the token until the API rejects it).
    /// </summary>
    [Test]
    public async Task FederatedProfile_Legacy_NoExpiresAt_NoRefresh_UsesBearer()
    {
        using var env = new TestEnv();
        env.Set("YT_API_BASE_URL", null);
        env.Set("YT_TIMEOUT", null);
        env.SetConfig("""
        {"default_profile":"fed","profiles":{"fed":{"org_type":"cloud","org_id":"o",
          "read_only":false,"auth":{"type":"federated","token":"t1.legacy","federation_id":"fed-1"}}}}
        """);
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));

        using var ctx = await TrackerContextFactory.CreateAsync(
            profileName: null,
            cliReadOnly: false,
            timeoutSeconds: null,
            innerHandler: inner);

        _ = await ctx.RawHttp.GetAsync("https://api.tracker.yandex.net/v3/myself");
        var req = inner.Seen[0];
        await Assert.That(req.Headers.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(req.Headers.Authorization.Parameter).IsEqualTo("t1.legacy");
    }

    /// <summary>
    /// Federated profile with neither <c>refresh_token</c> nor access <c>token</c> →
    /// <see cref="ErrorCode.AuthFailed"/> with a re-login instruction (exit 4).
    /// </summary>
    [Test]
    public async Task FederatedProfile_WithoutRefreshToken_AndWithoutAccess_AuthFailed()
    {
        using var env = new TestEnv();
        env.Set("YT_API_BASE_URL", null);
        env.Set("YT_TIMEOUT", null);
        env.SetConfig("""
        {"default_profile":"fed-empty","profiles":{"fed-empty":{"org_type":"cloud","org_id":"o",
          "read_only":false,"auth":{"type":"federated","federation_id":"fed-1"}}}}
        """);

        var ex = await Assert.ThrowsAsync<TrackerException>(
            () => TrackerContextFactory.CreateAsync(null, false, null));
        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.AuthFailed);
        await Assert.That(ex.Message).Contains("--profile fed-empty");
    }

    /// <summary>
    /// Federated profile where <c>access_token_expires_at</c> is malformed → ConfigError
    /// (don't silently fall through to "trust the token forever").
    /// </summary>
    [Test]
    public async Task FederatedProfile_InvalidExpiresAt_ConfigError()
    {
        using var env = new TestEnv();
        env.Set("YT_API_BASE_URL", null);
        env.Set("YT_TIMEOUT", null);
        env.SetConfig("""
        {"default_profile":"fed","profiles":{"fed":{"org_type":"cloud","org_id":"o",
          "read_only":false,"auth":{"type":"federated","token":"t1.x","federation_id":"fed-1",
          "access_token_expires_at":"not-a-date"}}}}
        """);

        var ex = await Assert.ThrowsAsync<TrackerException>(
            () => TrackerContextFactory.CreateAsync(null, false, null));
        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.ConfigError);
        await Assert.That(ex.Message).Contains("not-a-date");
    }

    private sealed class FakeExchange : IIamExchangeClient
    {
        private readonly Func<string, IamExchangeResult> _impl;
        public FakeExchange(Func<string, IamExchangeResult> impl) => _impl = impl;
        public Task<IamExchangeResult> ExchangeAsync(string jwt, CancellationToken ct) =>
            Task.FromResult(_impl(jwt));
    }
}
