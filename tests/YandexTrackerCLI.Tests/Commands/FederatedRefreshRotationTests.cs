using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands;

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;
using YandexTrackerCLI.Auth.Federated;

/// <summary>
/// End-to-end тесты ротации refresh-токена в federated-профиле: новый токен из ответа
/// сервера обязан попасть в профиль на диске, иначе после первого же успешного обновления
/// сохранённый токен мёртв и следующий запуск уедет в браузерный перелогин.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class FederatedRefreshRotationTests
{
    /// <summary>
    /// Refresh-клиент, отдающий заранее заданный ответ и запоминающий предъявленный токен.
    /// </summary>
    private sealed class StubRefresh : IFederatedRefreshClient
    {
        private readonly FederatedTokenResult _result;

        public StubRefresh(FederatedTokenResult result) => _result = result;

        public List<string> SeenRefreshTokens { get; } = new();

        public Task<FederatedTokenResult> Refresh(string refreshToken, string clientId, ECDsa key, CancellationToken ct)
        {
            SeenRefreshTokens.Add(refreshToken);
            return Task.FromResult(_result);
        }
    }

    /// <summary>
    /// Сток, который всегда падает — имитирует занятый лок / отказ записи.
    /// </summary>
    private sealed class FailingSink : IRefreshTokenSink
    {
        public Task SaveRefreshToken(string refreshToken, CancellationToken ct) =>
            Task.FromException(new IOException("config file is not writable"));
    }

    private static string FedConfig(string root, string refreshToken) =>
        $$"""
          {
            "default_profile": "other",
            "profiles": {
              "other": {
                "org_type": "yandex360", "org_id": "other-org", "read_only": false,
                "auth": { "type": "oauth", "token": "other-token" }
              },
              "fed": {
                "org_type": "cloud", "org_id": "o1", "read_only": false, "default_format": "json",
                "allowed_queues": [ "DEV" ],
                "auth": {
                  "type": "federated", "token": "old-access", "refresh_token": "{{refreshToken}}",
                  "federation_id": "fed-1", "dpop_key_path": "{{root.Replace("\\", "/")}}/dpop.pem",
                  "access_token_expires_at": "2020-01-01T00:00:00.0000000+00:00"
                }
              }
            }
          }
          """;

    private static TestHttpMessageHandler ApiOk() =>
        new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"self":"https://.../myself","login":"me","uid":42}""",
                Encoding.UTF8,
                "application/json"),
        });

    /// <summary>
    /// Сервер провернул refresh-токен при обновлении — новый токен лежит в профиле,
    /// остальные поля профиля и соседний профиль не тронуты.
    /// </summary>
    [Test]
    public async Task RotatedRefreshToken_IsPersistedToProfile()
    {
        using var env = new TestEnv();
        env.SetConfig(FedConfig(env.Root, "rt-old"));
        env.InnerHandler = ApiOk();

        var refresh = new StubRefresh(
            new FederatedTokenResult("iam-fresh", "rt-rotated", DateTimeOffset.UtcNow.AddHours(1)));
        TrackerContextFactory.TestFederatedRefreshOverride.Value = refresh;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "--profile", "fed", "user", "me" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(refresh.SeenRefreshTokens).IsEquivalentTo(new[] { "rt-old" });

        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var root = saved.RootElement;

        var auth = root.GetProperty("profiles").GetProperty("fed").GetProperty("auth");
        await Assert.That(auth.GetProperty("refresh_token").GetString()).IsEqualTo("rt-rotated");

        // Всё остальное переживает запись: сохраняется только refresh-токен.
        await Assert.That(auth.GetProperty("federation_id").GetString()).IsEqualTo("fed-1");
        await Assert.That(auth.GetProperty("token").GetString()).IsEqualTo("old-access");
        await Assert.That(root.GetProperty("default_profile").GetString()).IsEqualTo("other");
        var fed = root.GetProperty("profiles").GetProperty("fed");
        await Assert.That(fed.GetProperty("org_id").GetString()).IsEqualTo("o1");
        await Assert.That(fed.GetProperty("allowed_queues").EnumerateArray().Select(e => e.GetString()!).ToArray())
            .IsEquivalentTo(new[] { "DEV" });
        var other = root.GetProperty("profiles").GetProperty("other").GetProperty("auth");
        await Assert.That(other.GetProperty("token").GetString()).IsEqualTo("other-token");
    }

    /// <summary>
    /// Сервер вернул тот же самый refresh-токен — конфиг на диске не переписывается:
    /// обновление идёт на горячем пути запросов, и лишняя запись под локом там не нужна.
    /// </summary>
    [Test]
    public async Task UnchangedRefreshToken_LeavesConfigFileUntouched()
    {
        using var env = new TestEnv();
        env.SetConfig(FedConfig(env.Root, "rt-old"));
        env.InnerHandler = ApiOk();

        // Исходный файл записан с отступами; любая перезапись через ConfigStore
        // сериализует его компактно, поэтому побайтовое сравнение ловит факт записи.
        var before = File.ReadAllBytes(env.ConfigPath);

        TrackerContextFactory.TestFederatedRefreshOverride.Value = new StubRefresh(
            new FederatedTokenResult("iam-fresh", "rt-old", DateTimeOffset.UtcNow.AddHours(1)));

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "--profile", "fed", "user", "me" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(File.ReadAllBytes(env.ConfigPath)).IsEquivalentTo(before);
    }

    /// <summary>
    /// Отказ сохранения не роняет команду: access-токен на руках валиден, команда
    /// доходит до конца, а в stderr появляется предупреждение о несохранённом токене.
    /// </summary>
    [Test]
    public async Task SinkFailure_CommandStillSucceeds_AndWarnsOnStderr()
    {
        using var env = new TestEnv();
        env.SetConfig(FedConfig(env.Root, "rt-old"));
        env.InnerHandler = ApiOk();

        TrackerContextFactory.TestFederatedRefreshOverride.Value = new StubRefresh(
            new FederatedTokenResult("iam-fresh", "rt-rotated", DateTimeOffset.UtcNow.AddHours(1)));
        TrackerContextFactory.TestRefreshTokenSinkOverride.Value = new FailingSink();

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "--profile", "fed", "user", "me" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var body = JsonDocument.Parse(sw.ToString());
        await Assert.That(body.RootElement.GetProperty("login").GetString()).IsEqualTo("me");

        var warningLine = er.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Single(l => l.Contains("refresh_token_not_saved", StringComparison.Ordinal));
        using var warning = JsonDocument.Parse(warningLine);
        var w = warning.RootElement.GetProperty("warning");
        await Assert.That(w.GetProperty("code").GetString()).IsEqualTo("refresh_token_not_saved");
        await Assert.That(w.GetProperty("message").GetString()!).Contains("re-login");
        await Assert.That(w.GetProperty("reason").GetString()!).Contains("not writable");
    }
}
