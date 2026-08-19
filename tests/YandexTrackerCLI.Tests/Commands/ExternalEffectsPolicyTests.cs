using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands;

using System.Net;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты политики профиля <c>external_effects</c>: «не инициировать рассылку и
/// интеграции явно». Запрещены призыв (<c>summonees</c>/<c>maillistSummonees</c> в теле на
/// любой вложенности) и мутации автоматизаций — вся группа <c>yt automation</c>
/// (<c>triggers</c>/<c>autoactions</c>/<c>macros</c>); чтение
/// не ограничено. Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ExternalEffectsPolicyTests
{
    private const string RestrictedConfig =
        """
        {
          "default_profile":"ci",
          "profiles":{"ci":{"org_type":"cloud","org_id":"o","read_only":false,
                            "external_effects":false,
                            "auth":{"type":"oauth","token":"y0_X"}}}
        }
        """;

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static async Task<(int Exit, string Stderr, TestHttpMessageHandler Inner)> RunWithStdin(
        TestEnv env,
        string stdin,
        params string[] args)
    {
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"id":"1"}"""));
        env.InnerHandler = inner;

        var prev = Console.In;
        Console.SetIn(new StringReader(stdin));
        try
        {
            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(args, sw, er);
            return (exit, er.ToString(), inner);
        }
        finally
        {
            Console.SetIn(prev);
        }
    }

    private static async Task AssertPolicyViolation(string stderr, string expectedFragment)
    {
        using var doc = JsonDocument.Parse(stderr);
        var error = doc.RootElement.GetProperty("error");
        await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("policy_violation");
        await Assert.That(error.GetProperty("message").GetString()!).Contains(expectedFragment);
        await Assert.That(error.GetProperty("message").GetString()!).Contains("external_effects");
    }

    /// <summary>
    /// Призыв в теле комментария — exit 10, запрос не уходит.
    /// </summary>
    [Test]
    [Arguments("""{"text":"hi","summonees":["someone"]}""", "summonees")]
    [Arguments("""{"text":"hi","maillistSummonees":["list@example.com"]}""", "maillistSummonees")]
    public async Task CommentAdd_Summon_IsBlocked(string body, string field)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);

        var (exit, stderr, inner) = await RunWithStdin(
            env, body, "comment", "add", "DEV-42", "--json-stdin");

        await Assert.That(exit).IsEqualTo(10);
        await AssertPolicyViolation(stderr, $"'{field}'");
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// Призыв во вложенном объекте (комментарий внутри тела перехода) тоже запрещён.
    /// </summary>
    [Test]
    public async Task Transition_NestedSummon_IsBlocked()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);

        var (exit, stderr, inner) = await RunWithStdin(
            env,
            """{"comment":{"text":"done","summonees":["someone"]}}""",
            "issue", "transition", "DEV-42", "--to", "close", "--json-stdin");

        await Assert.That(exit).IsEqualTo(10);
        await AssertPolicyViolation(stderr, "'summonees'");
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// Обычный комментарий без призыва проходит: политика узкая.
    /// </summary>
    [Test]
    public async Task CommentAdd_WithoutSummon_Succeeds()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"id":"1"}"""));
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "add", "DEV-42", "--text", "hi" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    /// <summary>
    /// <c>YT_EXTERNAL_EFFECTS</c> ужесточает поверх профиля без ограничения.
    /// </summary>
    [Test]
    [Arguments("0")]
    [Arguments("false")]
    [Arguments("off")]
    public async Task Env_Tightens_OverUnrestrictedProfile(string raw)
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        env.Set("YT_EXTERNAL_EFFECTS", raw);

        var (exit, stderr, inner) = await RunWithStdin(
            env,
            """{"text":"hi","summonees":["someone"]}""",
            "comment", "add", "DEV-42", "--json-stdin");

        await Assert.That(exit).IsEqualTo(10);
        await AssertPolicyViolation(stderr, "'summonees'");
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// …но снять запрет профиля переменная не может.
    /// </summary>
    [Test]
    [Arguments("1")]
    [Arguments("true")]
    public async Task Env_CannotLift_ProfilePolicy(string raw)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        env.Set("YT_EXTERNAL_EFFECTS", raw);

        var (exit, stderr, inner) = await RunWithStdin(
            env,
            """{"text":"hi","summonees":["someone"]}""",
            "comment", "add", "DEV-42", "--json-stdin");

        await Assert.That(exit).IsEqualTo(10);
        await AssertPolicyViolation(stderr, "'summonees'");
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// Нераспознанное значение переменной — <c>config_error</c>, а не молчаливое
    /// «ограничения нет».
    /// </summary>
    [Test]
    public async Task Env_Unrecognized_IsConfigError()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        env.Set("YT_EXTERNAL_EFFECTS", "enabled");
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "add", "DEV-42", "--text", "hi" }, sw, er);

        await Assert.That(exit).IsEqualTo(9);
        using var doc = JsonDocument.Parse(er.ToString());
        var error = doc.RootElement.GetProperty("error");
        await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("config_error");
        await Assert.That(error.GetProperty("message").GetString()!).Contains("YT_EXTERNAL_EFFECTS");
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// <c>yt config set external_effects false</c> — ужесточение, проходит.
    /// </summary>
    [Test]
    public async Task ConfigSet_CanDisableExternalEffects()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "config", "set", "external_effects", "false" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        await Assert.That(saved.RootElement.GetProperty("profiles").GetProperty("default")
            .GetProperty("external_effects").GetBoolean()).IsFalse();
    }

    /// <summary>
    /// Обратное — отказ: политику снимают пересозданием профиля, а не той же командой,
    /// которой располагает ограничиваемый вызывающий.
    /// </summary>
    [Test]
    [Arguments("true")]
    [Arguments("1")]
    public async Task ConfigSet_CannotLiftExternalEffects(string value)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "config", "set", "external_effects", value }, sw, er);

        await Assert.That(exit).IsEqualTo(10);
        using var doc = JsonDocument.Parse(er.ToString());
        var error = doc.RootElement.GetProperty("error");
        await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("policy_violation");
        await Assert.That(error.GetProperty("message").GetString()!).Contains("external_effects");
        // Отказ обязан назвать путь сброса, иначе пользователь застревает.
        await Assert.That(error.GetProperty("message").GetString()!).Contains("yt auth login");

        // Файл конфига не изменился.
        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        await Assert.That(saved.RootElement.GetProperty("profiles").GetProperty("ci")
            .GetProperty("external_effects").GetBoolean()).IsFalse();
    }

    /// <summary>
    /// Мутации автоматизаций запрещены через реальные команды — по всей группе
    /// <c>yt automation</c>, включая макросы: триггер, автодействие и макрос одинаково
    /// переживают сессию и правятся на очереди.
    /// </summary>
    /// <param name="commandLine">Командная строка (аргументы через пробел).</param>
    [Test]
    [Arguments("automation trigger create --queue DEV --name t")]
    [Arguments("automation trigger update 7 --queue DEV --name t")]
    [Arguments("automation trigger delete 7 --queue DEV")]
    // Флаг --version дописывает ?version=N к пути: query не должен уводить запрос
    // мимо guard-пайплайна (сегменты сверяются после отделения query).
    [Arguments("automation trigger deactivate 7 --queue DEV --version 3")]
    [Arguments("automation autoaction update 7 --queue DEV --name a --version 3")]
    [Arguments("automation autoaction activate 7 --queue DEV --version 3")]
    [Arguments("automation autoaction create --queue DEV --name a")]
    [Arguments("automation autoaction delete 7 --queue DEV")]
    [Arguments("automation macro create --queue DEV --name m")]
    [Arguments("automation macro update 7 --queue DEV --name m")]
    [Arguments("automation macro delete 7 --queue DEV")]
    public async Task AutomationMutation_IsBlocked(string commandLine)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(commandLine.Split(' '), sw, er);

        await Assert.That(exit).IsEqualTo(10);
        await AssertPolicyViolation(er.ToString(), "queues/DEV/");
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// Чтение автоматизаций теми же командами проходит: политика ограничивает запись.
    /// </summary>
    /// <param name="response">Ответ, который отдаёт сервер.</param>
    /// <param name="args">Аргументы команды.</param>
    [Test]
    [Arguments("[]", new[] { "automation", "trigger", "list", "--queue", "DEV" })]
    [Arguments("[]", new[] { "automation", "autoaction", "list", "--queue", "DEV" })]
    [Arguments("[]", new[] { "automation", "macro", "list", "--queue", "DEV" })]
    [Arguments("""{"id":7}""", new[] { "automation", "trigger", "get", "7", "--queue", "DEV" })]
    [Arguments("""{"id":7}""", new[] { "automation", "macro", "get", "7", "--queue", "DEV" })]
    public async Task AutomationRead_Succeeds(string response, string[] args)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json(response));
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(args, sw, er);

        await Assert.That(er.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    /// <summary>
    /// Без ограничения те же мутации уходят наружу — иначе предыдущий тест доказывал бы
    /// лишь то, что команда не работает вовсе.
    /// </summary>
    [Test]
    public async Task AutomationMutation_PassesThrough_WhenPolicyIsOff()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"id":7}"""));
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "automation", "macro", "create", "--queue", "DEV", "--name", "m" }, sw, er);

        await Assert.That(er.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    /// <summary>
    /// Действующая политика видна в <c>yt auth status</c>.
    /// </summary>
    [Test]
    public async Task AuthStatus_ReportsExternalEffects()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "auth", "status" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("external_effects").GetBoolean()).IsFalse();
    }
}
