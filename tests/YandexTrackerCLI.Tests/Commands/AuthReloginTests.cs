using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands;

using System.Net;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;
using YandexTrackerCLI.Auth.Federated;
using YandexTrackerCLI.Commands.Auth;
using YandexTrackerCLI.Interactive;

/// <summary>
/// End-to-end тесты команды <c>yt auth relogin</c>: повторный браузерный вход для
/// существующего federated-профиля с переиспользованием federation_id и DPoP-ключа.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class AuthReloginTests
{
    private sealed class CapturingBrowser : IBrowserLauncher
    {
        public string? Url { get; private set; }

        public Task OpenAsync(string url, CancellationToken ct)
        {
            Url = url;
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task Relogin_FederatedProfile_CompletesFlow_AndUpdatesTokens()
    {
        using var env = new TestEnv();

        // Two profiles, a non-default `default_profile`, and on the relogin'd profile a
        // non-default DefaultFormat (json) plus read_only=true — so we can prove that
        // relogin updates only the tokens and leaves everything else (and the sibling
        // profile) byte-for-byte intact.
        env.SetConfig(
            """
            {"default_profile":"other","profiles":{
              "other":{"org_type":"yandex360","org_id":"other-org","read_only":false,
                "auth":{"type":"oauth","token":"other-token"}},
              "fed":{"org_type":"cloud","org_id":"o1","read_only":true,"default_format":"json",
                "auth":{"type":"federated","token":"old-access","refresh_token":"rt-old",
                  "federation_id":"fed-1","dpop_key_path":null,"access_token_expires_at":"2020-01-01T00:00:00.0000000+00:00"}}}}
            """);

        var browser = new CapturingBrowser();
        FederatedReloginService.TestBrowserLauncher.Value = browser;

        var callbackTask = Task.Run(async () =>
        {
            while (browser.Url is null)
            {
                await Task.Delay(10);
            }

            var uri = new Uri(browser.Url!);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var redirect = query["redirect_uri"]!;
            var state = query["state"]!;
            using var http = new HttpClient();
            await http.GetAsync($"{redirect}?code=FAKE_CODE&state={state}");
        });

        var fakeHandler = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"iam-relogin-new","refresh_token":"rt-new","expires_in":43199}""",
                Encoding.UTF8,
                "application/json"),
        });
        FederatedReloginService.TestFederatedHttpClient.Value = new HttpClient(fakeHandler);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "--profile", "fed", "auth", "relogin", "--timeout-auth", "10" },
            sw,
            er);

        await callbackTask;
        await Assert.That(exit).IsEqualTo(0);

        // Reuses the stored federation_id (no --federation-id passed).
        await Assert.That(browser.Url!).Contains("yc_federation_hint=fed-1");
        await Assert.That(browser.Url!).Contains("client_id=yc.oauth.public-sdk");

        // Saved marker on stdout with mode=federated.
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("saved").GetString()).IsEqualTo("fed");
        await Assert.That(doc.RootElement.GetProperty("mode").GetString()).IsEqualTo("federated");

        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var root = saved.RootElement;

        // default_profile is preserved (relogin targeted "fed", not the default).
        await Assert.That(root.GetProperty("default_profile").GetString()).IsEqualTo("other");

        // The sibling profile is left completely untouched.
        var other = root.GetProperty("profiles").GetProperty("other");
        await Assert.That(other.GetProperty("org_type").GetString()).IsEqualTo("yandex360");
        await Assert.That(other.GetProperty("org_id").GetString()).IsEqualTo("other-org");
        await Assert.That(other.GetProperty("read_only").GetBoolean()).IsFalse();
        var otherAuth = other.GetProperty("auth");
        await Assert.That(otherAuth.GetProperty("type").GetString()).IsEqualTo("oauth");
        await Assert.That(otherAuth.GetProperty("token").GetString()).IsEqualTo("other-token");

        // The relogin'd profile: tokens updated, every other field preserved.
        var profile = root.GetProperty("profiles").GetProperty("fed");
        await Assert.That(profile.GetProperty("org_type").GetString()).IsEqualTo("cloud");
        await Assert.That(profile.GetProperty("org_id").GetString()).IsEqualTo("o1");
        await Assert.That(profile.GetProperty("read_only").GetBoolean()).IsTrue();
        await Assert.That(profile.GetProperty("default_format").GetString()).IsEqualTo("json");

        var auth = profile.GetProperty("auth");
        await Assert.That(auth.GetProperty("type").GetString()).IsEqualTo("federated");
        await Assert.That(auth.GetProperty("token").GetString()).IsEqualTo("iam-relogin-new");
        await Assert.That(auth.GetProperty("refresh_token").GetString()).IsEqualTo("rt-new");
        await Assert.That(auth.GetProperty("access_token_expires_at").GetString())
            .IsNotEqualTo("2020-01-01T00:00:00.0000000+00:00");
        await Assert.That(auth.GetProperty("federation_id").GetString()).IsEqualTo("fed-1");
        await Assert.That(auth.GetProperty("dpop_key_path").GetString()!.Length > 0).IsTrue();
    }

    [Test]
    public async Task Relogin_OAuthProfile_Returns_InvalidArgs()
    {
        using var env = new TestEnv();
        env.SetConfig(
            """
            {"default_profile":"default","profiles":{
              "oauthp":{"org_type":"cloud","org_id":"o","read_only":false,
                "auth":{"type":"oauth","token":"y0_X"}}}}
            """);

        FederatedReloginService.TestBrowserLauncher.Value = new CapturingBrowser();
        FederatedReloginService.TestFederatedHttpClient.Value = new HttpClient(
            new TestHttpMessageHandler());

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "--profile", "oauthp", "auth", "relogin" },
            sw,
            er);

        // InvalidArgs → exit 2, structured error on stderr.
        await Assert.That(exit).IsEqualTo(2);
        using var werr = JsonDocument.Parse(er.ToString());
        await Assert.That(werr.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
    }
}
