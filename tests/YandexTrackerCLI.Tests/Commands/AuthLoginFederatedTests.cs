using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands;

using System.Net;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;
using YandexTrackerCLI.Commands.Auth;
using YandexTrackerCLI.Interactive;

/// <summary>
/// End-to-end тесты команды <c>yt auth login --type federated</c>:
/// проверяют полный PKCE-flow через фейковые <see cref="IBrowserLauncher"/> и
/// перехват HTTP token exchange через <see cref="TestHttpMessageHandler"/>.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class AuthLoginFederatedTests
{
    /// <summary>
    /// Фейковый браузер: запоминает URL и вызывает callback через фоновый HTTP GET,
    /// имитируя возврат пользователя на <c>redirect_uri</c>.
    /// </summary>
    private sealed class CapturingBrowser : IBrowserLauncher
    {
        public string? Url { get; private set; }

        public Task OpenAsync(string url, CancellationToken ct)
        {
            Url = url;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Фейковый stdin-reader для тестов.
    /// </summary>
    private sealed class FakeReader : ITokenReader
    {
        public bool IsInputRedirected { get; }

        public FakeReader(bool isRedirected) => IsInputRedirected = isRedirected;

        public string? ReadLine() => null;
    }

    [Test]
    public async Task Federated_Interactive_CompletesFlow_AndSavesProfile()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");

        var browser = new CapturingBrowser();
        AuthLoginCommand.TestBrowserLauncher.Value = browser;
        AuthLoginCommand.TestTokenReader.Value = new FakeReader(isRedirected: false);

        // Фоновая имитация браузера: как только CLI откроет URL — делаем callback.
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

        // Фейковый token endpoint.
        var fakeHandler = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"iam-federated-123","refresh_token":"rt-XX","expires_in":3600}""",
                    Encoding.UTF8,
                    "application/json"),
            };
            return r;
        });
        AuthLoginCommand.TestFederatedHttpClient.Value = new HttpClient(fakeHandler);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[]
            {
                "--profile", "ci",
                "auth", "login", "--type", "federated", "--federation-id", "fed-1",
                "--org-type", "cloud", "--org-id", "o1", "--timeout-auth", "10",
            },
            sw,
            er);

        await callbackTask;
        await Assert.That(exit).IsEqualTo(0);

        await Assert.That(browser.Url!).Contains("auth.yandex.cloud/oauth/authorize");
        await Assert.That(browser.Url!).Contains("scope=openid");
        await Assert.That(browser.Url!).Contains("client_id=yc.oauth.public-sdk");

        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var auth = saved.RootElement
            .GetProperty("profiles").GetProperty("ci")
            .GetProperty("auth");
        await Assert.That(auth.GetProperty("token").GetString()).IsEqualTo("iam-federated-123");
        await Assert.That(auth.GetProperty("refresh_token").GetString()).IsEqualTo("rt-XX");
        await Assert.That(auth.GetProperty("federation_id").GetString()).IsEqualTo("fed-1");
    }

    [Test]
    public async Task Federated_WithRefreshToken_Stdout_Contains_FederatedMode_NoWarning()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");

        var browser = new CapturingBrowser();
        AuthLoginCommand.TestBrowserLauncher.Value = browser;
        AuthLoginCommand.TestTokenReader.Value = new FakeReader(isRedirected: false);

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
                """{"access_token":"iam-fed-with-rt","refresh_token":"rt-bound","expires_in":43199}""",
                Encoding.UTF8,
                "application/json"),
        });
        AuthLoginCommand.TestFederatedHttpClient.Value = new HttpClient(fakeHandler);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[]
            {
                "--profile", "fed-full",
                "auth", "login", "--type", "federated", "--federation-id", "fed-1",
                "--org-type", "cloud", "--org-id", "o1", "--timeout-auth", "10",
            },
            sw,
            er);

        await callbackTask;
        await Assert.That(exit).IsEqualTo(0);

        // Success marker on stdout includes mode: federated and the expiry.
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("saved").GetString()).IsEqualTo("fed-full");
        await Assert.That(doc.RootElement.GetProperty("mode").GetString()).IsEqualTo("federated");
        await Assert.That(doc.RootElement.TryGetProperty("access_token_expires_at", out var exp)).IsTrue();
        await Assert.That(string.IsNullOrWhiteSpace(exp.GetString())).IsFalse();

        // No warning was emitted because refresh_token was issued.
        await Assert.That(er.ToString().Contains("no_refresh_token")).IsFalse();

        // Persisted profile carries access_token_expires_at and refresh_token.
        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var auth = saved.RootElement
            .GetProperty("profiles").GetProperty("fed-full")
            .GetProperty("auth");
        await Assert.That(auth.GetProperty("token").GetString()).IsEqualTo("iam-fed-with-rt");
        await Assert.That(auth.GetProperty("refresh_token").GetString()).IsEqualTo("rt-bound");
        await Assert.That(auth.TryGetProperty("access_token_expires_at", out var savedExp)).IsTrue();
        await Assert.That(string.IsNullOrWhiteSpace(savedExp.GetString())).IsFalse();
    }

    [Test]
    public async Task Federated_WithoutRefreshToken_Stdout_Contains_FederatedStaticMode_AndStderrWarning()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");

        var browser = new CapturingBrowser();
        AuthLoginCommand.TestBrowserLauncher.Value = browser;
        AuthLoginCommand.TestTokenReader.Value = new FakeReader(isRedirected: false);

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

        // Yandex Cloud responds 200 with access_token but NO refresh_token (the org has
        // refresh-token issuance disabled, or DPoP wasn't honored — both are real cases).
        var fakeHandler = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"iam-only-12h","scope":"openid","token_type":"Bearer","expires_in":43199}""",
                Encoding.UTF8,
                "application/json"),
        });
        AuthLoginCommand.TestFederatedHttpClient.Value = new HttpClient(fakeHandler);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[]
            {
                "--profile", "fed-static",
                "auth", "login", "--type", "federated", "--federation-id", "fed-1",
                "--org-type", "cloud", "--org-id", "o1", "--timeout-auth", "10",
            },
            sw,
            er);

        await callbackTask;
        await Assert.That(exit).IsEqualTo(0);

        // Stdout: success marker with mode=federated_static and expires_at.
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("saved").GetString()).IsEqualTo("fed-static");
        await Assert.That(doc.RootElement.GetProperty("mode").GetString()).IsEqualTo("federated_static");
        await Assert.That(doc.RootElement.TryGetProperty("access_token_expires_at", out var exp)).IsTrue();
        var stdoutExpiry = exp.GetString()!;
        await Assert.That(string.IsNullOrWhiteSpace(stdoutExpiry)).IsFalse();

        // Stderr: structured warning with the same expiry.
        using var werr = JsonDocument.Parse(er.ToString());
        var warning = werr.RootElement.GetProperty("warning");
        await Assert.That(warning.GetProperty("code").GetString()).IsEqualTo("no_refresh_token");
        await Assert.That(warning.GetProperty("message").GetString()).Contains("Re-login will be required");
        await Assert.That(warning.GetProperty("access_token_expires_at").GetString()).IsEqualTo(stdoutExpiry);

        // Persisted profile has access token + expires_at, no refresh.
        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var auth = saved.RootElement
            .GetProperty("profiles").GetProperty("fed-static")
            .GetProperty("auth");
        await Assert.That(auth.GetProperty("type").GetString()).IsEqualTo("federated");
        await Assert.That(auth.GetProperty("token").GetString()).IsEqualTo("iam-only-12h");
        await Assert.That(auth.TryGetProperty("refresh_token", out var rt)).IsFalse();
        await Assert.That(auth.GetProperty("access_token_expires_at").GetString()).IsEqualTo(stdoutExpiry);
    }

    [Test]
    public async Task Federated_NonTty_Returns_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");
        AuthLoginCommand.TestTokenReader.Value = new FakeReader(isRedirected: true);
        AuthLoginCommand.TestBrowserLauncher.Value = new CapturingBrowser();

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[]
            {
                "auth", "login", "--type", "federated", "--federation-id", "x",
                "--org-type", "cloud", "--org-id", "o",
            },
            sw,
            er);
        await Assert.That(exit).IsEqualTo(2);
    }

    [Test]
    public async Task Federated_MissingFederationId_Returns_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");
        AuthLoginCommand.TestTokenReader.Value = new FakeReader(isRedirected: false);
        AuthLoginCommand.TestBrowserLauncher.Value = new CapturingBrowser();

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[]
            {
                "auth", "login", "--type", "federated",
                "--org-type", "cloud", "--org-id", "o",
            },
            sw,
            er);
        await Assert.That(exit).IsEqualTo(2);
    }
}
