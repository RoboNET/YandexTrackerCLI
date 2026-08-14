namespace YandexTrackerCLI.Core.Tests.Http;

using System.Net;
using TUnit.Core;
using YandexTrackerCLI.Core.Api.Errors;
using YandexTrackerCLI.Core.Auth;
using YandexTrackerCLI.Core.Config;
using YandexTrackerCLI.Core.Http;

public sealed class TrackerHttpClientFactoryTests
{
    [Test]
    public async Task Factory_AddsAuth_OrgHeader_OnGet()
    {
        var captured = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var profile = new EffectiveProfile(
            Name: "test",
            OrgType: OrgType.Cloud,
            OrgId: "org-1",
            ReadOnly: false,
            Auth: new AuthConfig(AuthType.OAuth, Token: "y0"),
            ExternalEffectsAllowed: true);

        using var http = TrackerHttpClientFactory.Create(
            profile,
            authProvider: new OAuthProvider("y0"),
            innerHandler: captured);

        using var resp = await http.GetAsync("https://api.tracker.yandex.net/v3/myself");

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var seen = captured.Seen[0];
        await Assert.That(seen.Headers.Authorization!.Scheme).IsEqualTo("OAuth");
        await Assert.That(seen.Headers.Authorization!.Parameter).IsEqualTo("y0");
        await Assert.That(seen.Headers.GetValues("X-Cloud-Org-ID").Single()).IsEqualTo("org-1");
    }

    [Test]
    public async Task Factory_ReadOnlyTrue_BlocksPost_WithTrackerException()
    {
        var captured = new TestHttpMessageHandler();
        var profile = new EffectiveProfile("t", OrgType.Cloud, "o", ReadOnly: true,
            new AuthConfig(AuthType.OAuth, Token: "y"), ExternalEffectsAllowed: true);

        using var http = TrackerHttpClientFactory.Create(
            profile, new OAuthProvider("y"), innerHandler: captured);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.tracker.yandex.net/v3/issues");
        var ex = await Assert.ThrowsAsync<TrackerException>(() => http.SendAsync(req));
        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.ReadOnlyMode);
        await Assert.That(captured.Seen.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Factory_UsesYandex360Header_WhenProfileYandex360()
    {
        var captured = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var profile = new EffectiveProfile("t", OrgType.Yandex360, "org-1", false,
            new AuthConfig(AuthType.OAuth, Token: "y"), ExternalEffectsAllowed: true);

        using var http = TrackerHttpClientFactory.Create(
            profile, new OAuthProvider("y"), innerHandler: captured);

        _ = await http.GetAsync("https://api.tracker.yandex.net/v3/myself");

        var seen = captured.Seen[0];
        await Assert.That(seen.Headers.Contains("X-Org-ID")).IsTrue();
        await Assert.That(seen.Headers.GetValues("X-Org-ID").Single()).IsEqualTo("org-1");
        await Assert.That(seen.Headers.Contains("X-Cloud-Org-ID")).IsFalse();
    }

    [Test]
    public async Task Factory_SetsBaseAddress_Timeout_UserAgent()
    {
        var captured = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var profile = new EffectiveProfile("t", OrgType.Cloud, "o", false,
            new AuthConfig(AuthType.OAuth, Token: "y"), ExternalEffectsAllowed: true);

        using var http = TrackerHttpClientFactory.Create(
            profile, new OAuthProvider("y"), innerHandler: captured,
            baseUrl: new Uri("https://example.com/v3/"),
            timeout: TimeSpan.FromSeconds(5));

        await Assert.That(http.BaseAddress).IsEqualTo(new Uri("https://example.com/v3/"));
        await Assert.That(http.Timeout).IsEqualTo(TimeSpan.FromSeconds(5));
        _ = await http.GetAsync("myself");
        await Assert.That(captured.Seen[0].Headers.UserAgent.ToString()).Contains("yandex-tracker-cli/");
    }

    /// <summary>
    /// <see cref="RetryHandler"/> — самый внешний хендлер, поэтому на повторной попытке
    /// ТОТ ЖЕ <see cref="HttpRequestMessage"/> проходит цепочку заново. Заголовки, которые
    /// хендлеры добавляют (а не присваивают), обязаны уйти РОВНО ОДИН раз в каждой попытке:
    /// два <c>X-Cloud-Org-ID</c> — невалидный заголовок, а два разных DPoP-доказательства
    /// (с разными <c>jti</c>) сервер обязан отвергнуть.
    /// </summary>
    [Test]
    public async Task Factory_OnRetry_SendsEachInjectedHeaderExactlyOnce()
    {
        var proofs = 0;
        DPoPHandler.ProofFactory.Value = (_, _) => $"proof-{Interlocked.Increment(ref proofs)}";
        try
        {
            var orgPerAttempt = new List<string[]>();
            var dpopPerAttempt = new List<string[]>();
            var authPerAttempt = new List<string[]>();

            HttpResponseMessage Record(HttpRequestMessage req, HttpStatusCode status)
            {
                orgPerAttempt.Add(Values(req, "X-Cloud-Org-ID"));
                dpopPerAttempt.Add(Values(req, "DPoP"));
                authPerAttempt.Add(Values(req, "Authorization"));
                var resp = new HttpResponseMessage(status);
                resp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return resp;
            }

            var captured = new TestHttpMessageHandler()
                .Push(req => Record(req, HttpStatusCode.ServiceUnavailable))
                .Push(req => Record(req, HttpStatusCode.OK));

            var profile = new EffectiveProfile("t", OrgType.Cloud, "org-1", false,
                new AuthConfig(AuthType.OAuth, Token: "y0"), ExternalEffectsAllowed: true);

            using var http = TrackerHttpClientFactory.Create(
                profile, new OAuthProvider("y0"), innerHandler: captured);

            using var resp = await http.GetAsync("https://api.tracker.yandex.net/v3/myself");

            await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(orgPerAttempt.Count).IsEqualTo(2);

            // Первая попытка.
            await Assert.That(orgPerAttempt[0]).IsEquivalentTo(new[] { "org-1" });
            await Assert.That(dpopPerAttempt[0]).IsEquivalentTo(new[] { "proof-1" });
            await Assert.That(authPerAttempt[0].Length).IsEqualTo(1);

            // Повторная попытка: заголовки заменены, а не накоплены.
            await Assert.That(orgPerAttempt[1]).IsEquivalentTo(new[] { "org-1" });
            await Assert.That(dpopPerAttempt[1]).IsEquivalentTo(new[] { "proof-2" });
            await Assert.That(authPerAttempt[1].Length).IsEqualTo(1);
        }
        finally
        {
            DPoPHandler.ProofFactory.Value = null;
        }
    }

    private static string[] Values(HttpRequestMessage req, string name) =>
        req.Headers.TryGetValues(name, out var v) ? v.ToArray() : [];
}
