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
            Auth: new AuthConfig(AuthType.OAuth, Token: "y0"));

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
            new AuthConfig(AuthType.OAuth, Token: "y"));

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
            new AuthConfig(AuthType.OAuth, Token: "y"));

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
            new AuthConfig(AuthType.OAuth, Token: "y"));

        using var http = TrackerHttpClientFactory.Create(
            profile, new OAuthProvider("y"), innerHandler: captured,
            baseUrl: new Uri("https://example.com/v3/"),
            timeout: TimeSpan.FromSeconds(5));

        await Assert.That(http.BaseAddress).IsEqualTo(new Uri("https://example.com/v3/"));
        await Assert.That(http.Timeout).IsEqualTo(TimeSpan.FromSeconds(5));
        _ = await http.GetAsync("myself");
        await Assert.That(captured.Seen[0].Headers.UserAgent.ToString()).Contains("yandex-tracker-cli/");
    }
}
