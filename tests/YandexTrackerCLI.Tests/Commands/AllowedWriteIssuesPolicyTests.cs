using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты политики профиля <c>allowed_write_issues</c> (issue #15): область
/// записи ограничена списком задач, чтение — нет; операции без доказуемой привязки к
/// разрешённой задаче запрещены; поля рассылки уведомлений в теле комментария отвергаются.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class AllowedWriteIssuesPolicyTests
{
    private const string RestrictedConfig =
        """
        {
          "default_profile":"ci",
          "profiles":{"ci":{"org_type":"cloud","org_id":"o","read_only":false,
                            "allowed_write_issues":["DEV-42"],
                            "auth":{"type":"oauth","token":"y0_X"}}}
        }
        """;

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static async Task<string> ErrorCodeOf(StringWriter stderr)
    {
        using var doc = JsonDocument.Parse(stderr.ToString());
        return await Task.FromResult(doc.RootElement.GetProperty("error").GetProperty("code").GetString()!);
    }

    /// <summary>
    /// Запись в разрешённую задачу проходит.
    /// </summary>
    [Test]
    public async Task CommentAdd_AllowedIssue_Succeeds()
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
    /// Запись в задачу вне списка — exit 10 <c>policy_violation</c>, запрос не уходит.
    /// </summary>
    [Test]
    public async Task CommentAdd_ForbiddenIssue_FailsWithPolicyViolation()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "add", "OPS-7", "--text", "hi" }, sw, er);

        await Assert.That(exit).IsEqualTo(10);
        await Assert.That(await ErrorCodeOf(er)).IsEqualTo("policy_violation");
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("message").GetString())
            .IsEqualTo("issue 'OPS-7' is outside allowed_write_issues of profile 'ci' (allowed: DEV-42)");
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// Ключевое отличие от read-only: чтение задачи, закрытой на запись, работает.
    /// </summary>
    [Test]
    public async Task IssueGet_ForbiddenForWriteIssue_IsStillReadable()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"key":"OPS-7"}"""));
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "get", "OPS-7" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    /// <summary>
    /// Поиск (<c>POST issues/_search</c>) — семантически чтение, проходит.
    /// </summary>
    [Test]
    public async Task IssueFind_PostSearch_PassesThrough()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = Json("""[{"key":"OPS-7"},{"key":"DEV-42"}]""");
            r.Headers.Add("X-Total-Pages", "1");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "find", "--yql", "Updated: today()" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        using var doc = JsonDocument.Parse(sw.ToString());
        var keys = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("key").GetString()!).ToArray();
        await Assert.That(keys).IsEquivalentTo(new[] { "OPS-7", "DEV-42" });
    }

    /// <summary>
    /// Создание задачи при действующем ограничении запрещено: доказать, что запрос бьёт
    /// по разрешённой задаче, невозможно.
    /// </summary>
    [Test]
    public async Task IssueCreate_IsBlocked()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "issue", "create", "--queue", "DEV", "--summary", "s" }, sw, er);

        await Assert.That(exit).IsEqualTo(10);
        await Assert.That(await ErrorCodeOf(er)).IsEqualTo("policy_violation");
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// Пакетное изменение (<c>POST bulkchange</c>) при действующем ограничении запрещено.
    /// </summary>
    [Test]
    public async Task IssueBatch_IsBlocked()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var stdin = new StringReader("""{"issues":["DEV-42"],"values":{"priority":"critical"}}""");
        var prev = Console.In;
        Console.SetIn(stdin);
        try
        {
            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(new[] { "issue", "batch", "--json-stdin" }, sw, er);

            await Assert.That(exit).IsEqualTo(10);
            await Assert.That(await ErrorCodeOf(er)).IsEqualTo("policy_violation");
            await Assert.That(inner.Seen).IsEmpty();
        }
        finally
        {
            Console.SetIn(prev);
        }
    }

    /// <summary>
    /// Percent-encoding и регистр ключа не дают обхода.
    /// </summary>
    [Test]
    [Arguments("%4FPS-7")]
    [Arguments("OPS%2D7")]
    public async Task CommentAdd_PercentEncodedKey_DoesNotBypassPolicy(string key)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "add", key, "--text", "hi" }, sw, er);

        await Assert.That(exit).IsEqualTo(10);
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// Регистр не помогает: <c>dev-42</c> — та же задача, что <c>DEV-42</c>, и она разрешена;
    /// <c>ops-7</c> так же запрещена, как <c>OPS-7</c>.
    /// </summary>
    [Test]
    public async Task CommentAdd_CaseInsensitiveKeyComparison()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"id":"1"}"""));
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "add", "dev-42", "--text", "hi" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        sw = new StringWriter();
        er = new StringWriter();
        exit = await env.Invoke(new[] { "comment", "add", "ops-7", "--text", "hi" }, sw, er);
        await Assert.That(exit).IsEqualTo(10);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    /// <summary>
    /// <c>YT_ALLOWED_WRITE_ISSUES</c> <b>заменяет</b> список профиля: задача из env
    /// становится разрешённой, задача из профиля — нет.
    /// </summary>
    [Test]
    public async Task EnvVariable_ReplacesProfileList()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        env.Set("YT_ALLOWED_WRITE_ISSUES", "OPS-7");
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"id":"1"}"""));
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "add", "OPS-7", "--text", "hi" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        // Задача из профиля списком из env вытеснена, а не добавлена к нему.
        sw = new StringWriter();
        er = new StringWriter();
        exit = await env.Invoke(new[] { "comment", "add", "DEV-42", "--text", "hi" }, sw, er);
        await Assert.That(exit).IsEqualTo(10);
        await Assert.That(await ErrorCodeOf(er)).IsEqualTo("policy_violation");
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    /// <summary>
    /// <c>YT_ALLOWED_WRITE_ISSUES</c> задаёт ограничение и в профиле без него.
    /// </summary>
    [Test]
    public async Task EnvVariable_AloneSetsTheRestriction()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        env.Set("YT_ALLOWED_WRITE_ISSUES", "DEV-42,DEV-43");
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "add", "OPS-7", "--text", "hi" }, sw, er);

        await Assert.That(exit).IsEqualTo(10);
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// Поля рассылки уведомлений отвергаются, когда приходят raw-телом через
    /// <c>--json-stdin</c>, а не только через флаги команды.
    /// </summary>
    [Test]
    [Arguments("""{"text":"hi","summonees":["someone"]}""")]
    [Arguments("""{"text":"hi","maillistSummonees":["list@example.com"]}""")]
    public async Task CommentAdd_NotificationFieldsInRawBody_AreBlocked(string body)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var prev = Console.In;
        Console.SetIn(new StringReader(body));
        try
        {
            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(new[] { "comment", "add", "DEV-42", "--json-stdin" }, sw, er);

            await Assert.That(exit).IsEqualTo(10);
            await Assert.That(await ErrorCodeOf(er)).IsEqualTo("policy_violation");
            await Assert.That(inner.Seen).IsEmpty();
        }
        finally
        {
            Console.SetIn(prev);
        }
    }

    /// <summary>
    /// Без действующего ограничения то же тело уходит на сервер без изменений.
    /// </summary>
    [Test]
    public async Task CommentAdd_NotificationFields_PassWithoutRestriction()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"id":"1"}"""));
        env.InnerHandler = inner;

        var prev = Console.In;
        Console.SetIn(new StringReader("""{"text":"hi","summonees":["someone"]}"""));
        try
        {
            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(new[] { "comment", "add", "OPS-7", "--json-stdin" }, sw, er);

            await Assert.That(exit).IsEqualTo(0);
            await Assert.That(inner.Seen.Count).IsEqualTo(1);
        }
        finally
        {
            Console.SetIn(prev);
        }
    }

    /// <summary>
    /// Регрессия: без <c>allowed_write_issues</c> поведение прежнее — запись в любую задачу,
    /// создание задач и bulkchange работают.
    /// </summary>
    [Test]
    public async Task NoRestriction_BehaviourIsUnchanged()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"id":"1"}""")).Push(_ => Json("""{"key":"OPS-8"}"""));
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "add", "OPS-7", "--text", "hi" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        sw = new StringWriter();
        exit = await env.Invoke(new[] { "issue", "create", "--queue", "OPS", "--summary", "s" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(2);
    }

    /// <summary>
    /// <c>yt config set/get allowed_write_issues</c> — round-trip; снять ограничение той же
    /// командой уже нельзя (только ужесточение), сброс идёт через пересоздание профиля
    /// командой <c>yt auth login</c>.
    /// </summary>
    [Test]
    public async Task ConfigSetGet_AllowedWriteIssues_RoundTripsAndIsClearedOnlyByRelogin()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "config", "set", "allowed_write_issues", " DEV-42 , DEV-43 ,, dev-42 " }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        sw = new StringWriter();
        exit = await env.Invoke(new[] { "config", "get", "allowed_write_issues" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using (var doc = JsonDocument.Parse(sw.ToString()))
        {
            await Assert.That(doc.RootElement.GetString()).IsEqualTo("DEV-42,DEV-43");
        }

        // Ограничение действует сразу.
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;
        sw = new StringWriter();
        exit = await env.Invoke(new[] { "comment", "add", "OPS-7", "--text", "hi" }, sw, er);
        await Assert.That(exit).IsEqualTo(10);

        // Снятие через config set отклоняется: политику можно только ужесточить.
        sw = new StringWriter();
        exit = await env.Invoke(new[] { "config", "set", "allowed_write_issues", string.Empty }, sw, er);
        await Assert.That(exit).IsEqualTo(10);

        // Заявленный путь сброса — завести профиль заново.
        sw = new StringWriter();
        exit = await env.Invoke(
            new[]
            {
                "auth", "login", "--type", "oauth", "--token", "y0_X",
                "--org-type", "cloud", "--org-id", "o",
            },
            sw,
            er);
        await Assert.That(exit).IsEqualTo(0);

        sw = new StringWriter();
        exit = await env.Invoke(new[] { "config", "get", "allowed_write_issues" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using (var doc = JsonDocument.Parse(sw.ToString()))
        {
            await Assert.That(doc.RootElement.GetString()).IsEqualTo(string.Empty);
        }

        inner.Push(_ => Json("""{"id":"1"}"""));
        sw = new StringWriter();
        exit = await env.Invoke(new[] { "comment", "add", "OPS-7", "--text", "hi" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
    }

    /// <summary>
    /// <c>yt auth status</c> показывает действующий список и его источник.
    /// </summary>
    [Test]
    public async Task AuthStatus_ShowsAllowedWriteIssues_FromProfile()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "auth", "status" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        var issues = doc.RootElement.GetProperty("allowed_write_issues")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        await Assert.That(issues).IsEquivalentTo(new[] { "DEV-42" });
        await Assert.That(doc.RootElement.GetProperty("allowed_write_issues_source").GetString())
            .IsEqualTo("profile");
    }

    /// <summary>
    /// Когда список пришёл из окружения, <c>auth status</c> говорит об этом явно.
    /// </summary>
    [Test]
    public async Task AuthStatus_ShowsAllowedWriteIssues_FromEnv()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        env.Set("YT_ALLOWED_WRITE_ISSUES", "OPS-7,OPS-8");

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "auth", "status" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        var issues = doc.RootElement.GetProperty("allowed_write_issues")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        await Assert.That(issues).IsEquivalentTo(new[] { "OPS-7", "OPS-8" });
        await Assert.That(doc.RootElement.GetProperty("allowed_write_issues_source").GetString())
            .IsEqualTo("env");
    }

    /// <summary>
    /// Без ограничения <c>auth status</c> печатает пустой список и источник <c>profile</c>.
    /// </summary>
    [Test]
    public async Task AuthStatus_NoRestriction_PrintsEmptyList()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "auth", "status" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("allowed_write_issues").GetArrayLength()).IsEqualTo(0);
        await Assert.That(doc.RootElement.GetProperty("allowed_write_issues_source").GetString())
            .IsEqualTo("profile");
    }
}
