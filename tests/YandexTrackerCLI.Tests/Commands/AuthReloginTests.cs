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

        /// <summary>
        /// Хук, выполняемый в момент открытия браузера — то есть строго внутри окна
        /// read-modify-write перелогина (конфиг уже прочитан, но ещё не записан).
        /// Тесты используют его, чтобы сымитировать правку конфига другим процессом.
        /// </summary>
        public Action? OnOpen { get; init; }

        public Task OpenAsync(string url, CancellationToken ct)
        {
            Url = url;
            OnOpen?.Invoke();
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
                "allowed_queues":["DEV"],"allowed_write_issues":["DEV-1"],
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

        // Политики профиля переживают повторный вход: relogin обновляет только токены.
        // Тот же путь срабатывает автоматически при неудачном DPoP-refresh, поэтому потеря
        // здесь означала бы молчаливое снятие ограничений посреди сессии.
        var queues = profile.GetProperty("allowed_queues")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        await Assert.That(queues).IsEquivalentTo(new[] { "DEV" });
        var writeIssues = profile.GetProperty("allowed_write_issues")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        await Assert.That(writeIssues).IsEquivalentTo(new[] { "DEV-1" });

        var auth = profile.GetProperty("auth");
        await Assert.That(auth.GetProperty("type").GetString()).IsEqualTo("federated");
        await Assert.That(auth.GetProperty("token").GetString()).IsEqualTo("iam-relogin-new");
        await Assert.That(auth.GetProperty("refresh_token").GetString()).IsEqualTo("rt-new");
        await Assert.That(auth.GetProperty("access_token_expires_at").GetString())
            .IsNotEqualTo("2020-01-01T00:00:00.0000000+00:00");
        await Assert.That(auth.GetProperty("federation_id").GetString()).IsEqualTo("fed-1");
        await Assert.That(auth.GetProperty("dpop_key_path").GetString()!.Length > 0).IsTrue();
    }

    /// <summary>
    /// Автоматический re-login при неудачном DPoP-refresh идёт через
    /// <see cref="FederatedReloginService.ReloginAsync"/> — тот же метод, что и
    /// <c>yt auth relogin</c>, но без участия пользователя. Проверяем именно его: политики
    /// профиля обязаны пережить перезапись, иначе ограниченный профиль теряет ограничения
    /// посреди сессии и молча.
    /// </summary>
    [Test]
    public async Task InlineRelogin_PreservesProfilePolicies()
    {
        using var env = new TestEnv();
        env.SetConfig(
            """
            {"default_profile":"fed","profiles":{
              "fed":{"org_type":"cloud","org_id":"o1","read_only":true,
                "allowed_queues":["DEV","QA"],"allowed_write_issues":["DEV-1"],"external_effects":false,
                "auth":{"type":"federated","token":"old-access","refresh_token":"rt-old",
                  "federation_id":"fed-1","dpop_key_path":null,"access_token_expires_at":"2020-01-01T00:00:00.0000000+00:00"}}}}
            """);

        var browser = new CapturingBrowser();
        var callbackTask = Task.Run(async () =>
        {
            while (browser.Url is null)
            {
                await Task.Delay(10);
            }

            var uri = new Uri(browser.Url!);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            using var http = new HttpClient();
            await http.GetAsync($"{query["redirect_uri"]}?code=FAKE_CODE&state={query["state"]}");
        });

        var fakeHandler = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"iam-inline-new","refresh_token":"rt-new","expires_in":43199}""",
                Encoding.UTF8,
                "application/json"),
        });

        using var exchangeHttp = new HttpClient(fakeHandler);
        var result = await FederatedReloginService.ReloginAsync(
            "fed",
            browser,
            NoopInteractiveUI.Instance,
            exchangeHttp,
            wireSink: null,
            timeout: TimeSpan.FromSeconds(10),
            ct: CancellationToken.None);

        await callbackTask;
        await Assert.That(result.AccessToken).IsEqualTo("iam-inline-new");

        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var profile = saved.RootElement.GetProperty("profiles").GetProperty("fed");
        await Assert.That(profile.GetProperty("read_only").GetBoolean()).IsTrue();
        var queues = profile.GetProperty("allowed_queues")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        await Assert.That(queues).IsEquivalentTo(new[] { "DEV", "QA" });
        var writeIssues = profile.GetProperty("allowed_write_issues")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        await Assert.That(writeIssues).IsEquivalentTo(new[] { "DEV-1" });
        // Позиционный конструктор Profile молча потерял бы это поле — а вместе с ним
        // и запрет внешних эффектов, посреди сессии и без единого сообщения.
        await Assert.That(profile.GetProperty("external_effects").GetBoolean()).IsFalse();
    }

    /// <summary>
    /// Браузерный flow длится минуты, и всё это время перелогин держит в памяти снимок
    /// конфига, прочитанный до его начала. Правка соседнего профиля, легшая на диск внутри
    /// этого окна, обязана пережить сохранение — иначе перелогин молча откатывает чужие
    /// креденшелы к состоянию «минуты назад».
    /// </summary>
    [Test]
    public async Task InlineRelogin_PreservesConcurrentEdit_ToOtherProfile()
    {
        using var env = new TestEnv();
        env.SetConfig(BaseConfig("fed", readOnly: false, externalEffects: "null", otherToken: "other-token"));

        await RunInlineRelogin(() =>
            File.WriteAllText(
                env.ConfigPath,
                BaseConfig("fed", readOnly: false, externalEffects: "null", otherToken: "other-token-rotated")));

        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var profiles = saved.RootElement.GetProperty("profiles");
        await Assert.That(profiles.GetProperty("other").GetProperty("auth").GetProperty("token").GetString())
            .IsEqualTo("other-token-rotated");
        await Assert.That(profiles.GetProperty("fed").GetProperty("auth").GetProperty("token").GetString())
            .IsEqualTo("iam-inline-new");
    }

    /// <summary>
    /// Ужесточение политики профиля (<c>read_only</c>, <c>external_effects</c>), выполненное
    /// внутри окна перелогина, обязано пережить сохранение. Направление отката тут худшее из
    /// возможных: пользователь сузил права, а профиль вернулся бы к более широким — причём
    /// автоматический перелогин случается сам, без участия пользователя.
    /// </summary>
    [Test]
    public async Task InlineRelogin_PreservesConcurrentPolicyTightening()
    {
        using var env = new TestEnv();
        env.SetConfig(BaseConfig("fed", readOnly: false, externalEffects: "null", otherToken: "other-token"));

        await RunInlineRelogin(() =>
            File.WriteAllText(
                env.ConfigPath,
                BaseConfig("fed", readOnly: true, externalEffects: "false", otherToken: "other-token")));

        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var fed = saved.RootElement.GetProperty("profiles").GetProperty("fed");
        await Assert.That(fed.GetProperty("read_only").GetBoolean()).IsTrue();
        await Assert.That(fed.GetProperty("external_effects").GetBoolean()).IsFalse();
        await Assert.That(fed.GetProperty("auth").GetProperty("token").GetString()).IsEqualTo("iam-inline-new");
    }

    /// <summary>
    /// Переключение default-профиля (<c>yt config profile</c>) внутри окна перелогина
    /// не должно откатываться: перелогин трогает только <c>auth</c> своего профиля.
    /// </summary>
    [Test]
    public async Task InlineRelogin_PreservesConcurrentDefaultProfileSwitch()
    {
        using var env = new TestEnv();
        env.SetConfig(BaseConfig("fed", readOnly: false, externalEffects: "null", otherToken: "other-token"));

        await RunInlineRelogin(() =>
            File.WriteAllText(
                env.ConfigPath,
                BaseConfig("other", readOnly: false, externalEffects: "null", otherToken: "other-token")));

        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        await Assert.That(saved.RootElement.GetProperty("default_profile").GetString()).IsEqualTo("other");
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

    /// <summary>
    /// Конфиг из двух профилей — federated <c>fed</c> и oauth <c>other</c> — с параметрами,
    /// которые тесты гонок меняют «сторонним процессом» внутри окна перелогина.
    /// </summary>
    private static string BaseConfig(string defaultProfile, bool readOnly, string externalEffects, string otherToken) =>
        $$"""
        {"default_profile":"{{defaultProfile}}","profiles":{
          "other":{"org_type":"yandex360","org_id":"other-org","read_only":false,
            "auth":{"type":"oauth","token":"{{otherToken}}"}
          },
          "fed":{"org_type":"cloud","org_id":"o1","read_only":{{(readOnly ? "true" : "false")}},
            "external_effects":{{externalEffects}},
            "auth":{"type":"federated","token":"old-access","refresh_token":"rt-old",
              "federation_id":"fed-1","dpop_key_path":null,"access_token_expires_at":"2020-01-01T00:00:00.0000000+00:00"}
          }
         }
        }
        """;

    /// <summary>
    /// Прогоняет inline-перелогин профиля <c>fed</c> с фейковым браузером и фейковым
    /// token-endpoint. <paramref name="onBrowserOpen"/> выполняется в момент открытия
    /// браузера — то есть внутри окна между чтением и записью конфига.
    /// </summary>
    private static async Task RunInlineRelogin(Action onBrowserOpen)
    {
        var browser = new CapturingBrowser { OnOpen = onBrowserOpen };
        var callbackTask = Task.Run(async () =>
        {
            while (browser.Url is null)
            {
                await Task.Delay(10);
            }

            var uri = new Uri(browser.Url!);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            using var http = new HttpClient();
            await http.GetAsync($"{query["redirect_uri"]}?code=FAKE_CODE&state={query["state"]}");
        });

        var fakeHandler = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"iam-inline-new","refresh_token":"rt-new","expires_in":43199}""",
                Encoding.UTF8,
                "application/json"),
        });

        using var exchangeHttp = new HttpClient(fakeHandler);
        await FederatedReloginService.ReloginAsync(
            "fed",
            browser,
            NoopInteractiveUI.Instance,
            exchangeHttp,
            wireSink: null,
            timeout: TimeSpan.FromSeconds(10),
            ct: CancellationToken.None);

        await callbackTask;
    }
}
