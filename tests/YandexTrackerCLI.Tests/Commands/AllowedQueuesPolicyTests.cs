using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты политики профиля <c>allowed_queues</c> (issue #14): HTTP-guard на
/// адресные обращения, пост-фильтрация выдачи поиска и списка очередей, работа
/// <c>yt config get/set allowed_queues</c> и отображение в <c>yt auth status</c>.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class AllowedQueuesPolicyTests
{
    private const string RestrictedConfig =
        """
        {
          "default_profile":"ci",
          "profiles":{"ci":{"org_type":"cloud","org_id":"o","read_only":false,
                            "allowed_queues":["DEV","QA"],
                            "auth":{"type":"oauth","token":"y0_X"}}}
        }
        """;

    private static HttpResponseMessage Json(string body)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        return r;
    }

    /// <summary>
    /// Прямое обращение к разрешённой очереди проходит как обычно.
    /// </summary>
    [Test]
    public async Task IssueGet_AllowedQueue_Succeeds()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"key":"DEV-1"}"""));
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "get", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    /// <summary>
    /// Обращение к запрещённой очереди — явная ошибка политики (exit 10), а не «not found».
    /// Запрос до сети не доходит.
    /// </summary>
    [Test]
    public async Task IssueGet_ForbiddenQueue_FailsWithPolicyViolation()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "get", "OPS-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(10);
        using var doc = JsonDocument.Parse(er.ToString());
        var error = doc.RootElement.GetProperty("error");
        await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("policy_violation");
        await Assert.That(error.GetProperty("message").GetString())
            .IsEqualTo("queue 'OPS' is outside allowed_queues of profile 'ci' (allowed: DEV, QA)");
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// Запрет действует и на запись: комментарий в запрещённую очередь блокируется.
    /// </summary>
    [Test]
    public async Task CommentAdd_ForbiddenQueue_FailsWithPolicyViolation()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "add", "OPS-1", "--text", "hi" }, sw, er);

        await Assert.That(exit).IsEqualTo(10);
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// Percent-encoding ключа не даёт обхода: guard декодирует сегменты URL.
    /// </summary>
    [Test]
    public async Task IssueGet_PercentEncodedKey_DoesNotBypassPolicy()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        // %4F — это 'O': команда экранирует ключ повторно, guard разэкранирует до сравнения.
        var exit = await env.Invoke(new[] { "issue", "get", "%4FPS-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(10);
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// Регрессия: без <c>allowed_queues</c> поведение прежнее — любая очередь доступна.
    /// </summary>
    [Test]
    public async Task IssueGet_NoAllowedQueues_IsUnrestricted()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"key":"OPS-1"}"""));
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "get", "OPS-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    /// <summary>
    /// Произвольный YQL продолжает работать, но задачи вне списка вырезаются из вывода.
    /// </summary>
    [Test]
    public async Task IssueFind_FreeFormYql_FiltersOutForeignQueues()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = Json("""[{"key":"DEV-1"},{"key":"OPS-7"},{"key":"QA-2"}]""");
            r.Headers.Add("X-Total-Pages", "1");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "find", "--yql", "Updated: today()" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        var keys = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("key").GetString()!).ToArray();
        await Assert.That(keys).IsEquivalentTo(new[] { "DEV-1", "QA-2" });
    }

    /// <summary>
    /// <c>--max</c> считает выданные наружу элементы, а не полученные от сервера:
    /// отфильтрованные задачи не занимают квоту, пагинация продолжается до лимита.
    /// </summary>
    [Test]
    public async Task IssueFind_Max_CountsEmittedItemsOnly()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = Json("""[{"key":"OPS-1"},{"key":"OPS-2"},{"key":"DEV-1"}]""");
            r.Headers.Add("X-Total-Pages", "2");
            return r;
        }).Push(_ =>
        {
            var r = Json("""[{"key":"OPS-3"},{"key":"QA-1"},{"key":"DEV-2"}]""");
            r.Headers.Add("X-Total-Pages", "2");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "issue", "find", "--yql", "Updated: today()", "--max", "2" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        var keys = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("key").GetString()!).ToArray();
        await Assert.That(keys).IsEquivalentTo(new[] { "DEV-1", "QA-1" });
        await Assert.That(inner.Seen.Count).IsEqualTo(2);
    }

    /// <summary>
    /// Simple-фильтр <c>--queue</c> с запрещённой очередью — явная ошибка политики,
    /// а не молча пустая выдача.
    /// </summary>
    [Test]
    public async Task IssueFind_ExplicitForbiddenQueueFilter_FailsWithPolicyViolation()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "find", "--queue", "OPS" }, sw, er);

        await Assert.That(exit).IsEqualTo(10);
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// <c>yt queue list</c> отдаёт только очереди из списка профиля.
    /// </summary>
    [Test]
    public async Task QueueList_FiltersOutForeignQueues()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = Json("""[{"key":"DEV"},{"key":"OPS"},{"key":"QA"}]""");
            r.Headers.Add("X-Total-Pages", "1");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "queue", "list" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        var keys = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("key").GetString()!).ToArray();
        await Assert.That(keys).IsEquivalentTo(new[] { "DEV", "QA" });
    }

    /// <summary>
    /// Очередь создаваемой задачи едет в теле запроса — проверка происходит в слое CLI.
    /// </summary>
    [Test]
    public async Task IssueCreate_ForbiddenQueue_FailsWithPolicyViolation()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "issue", "create", "--queue", "OPS", "--summary", "s" }, sw, er);

        await Assert.That(exit).IsEqualTo(10);
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// Перенос задачи в запрещённую очередь блокируется, хотя исходная очередь разрешена.
    /// </summary>
    [Test]
    public async Task IssueMove_ToForbiddenQueue_FailsWithPolicyViolation()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "issue", "move", "DEV-1", "--to-queue", "OPS" }, sw, er);

        await Assert.That(exit).IsEqualTo(10);
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// <c>yt config set allowed_queues DEV,QA</c> + <c>yt config get allowed_queues</c>
    /// возвращает список через запятую; снять ограничение той же командой уже нельзя
    /// (только ужесточение) — сброс идёт через пересоздание профиля <c>yt auth login</c>.
    /// </summary>
    [Test]
    public async Task ConfigSetGet_AllowedQueues_RoundTripsAndIsClearedOnlyByRelogin()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "config", "set", "allowed_queues", " DEV , QA ,, dev " }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        sw = new StringWriter();
        exit = await env.Invoke(new[] { "config", "get", "allowed_queues" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using (var doc = JsonDocument.Parse(sw.ToString()))
        {
            await Assert.That(doc.RootElement.GetString()).IsEqualTo("DEV,QA");
        }

        // Снятие через config set отклоняется: политику можно только ужесточить.
        sw = new StringWriter();
        exit = await env.Invoke(new[] { "config", "set", "allowed_queues", string.Empty }, sw, er);
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
        exit = await env.Invoke(new[] { "config", "get", "allowed_queues" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using (var doc = JsonDocument.Parse(sw.ToString()))
        {
            await Assert.That(doc.RootElement.GetString()).IsEqualTo(string.Empty);
        }

        // Ограничение снято — обращение к любой очереди снова проходит.
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"key":"OPS-1"}"""));
        env.InnerHandler = inner;
        sw = new StringWriter();
        exit = await env.Invoke(new[] { "issue", "get", "OPS-1" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
    }

    /// <summary>
    /// <c>yt config set allowed_queues</c> сразу влияет на действующую политику.
    /// </summary>
    [Test]
    public async Task ConfigSet_AllowedQueues_TakesEffectImmediately()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "config", "set", "allowed_queues", "DEV" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;
        sw = new StringWriter();
        exit = await env.Invoke(new[] { "issue", "get", "OPS-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(10);
        await Assert.That(inner.Seen).IsEmpty();
    }

    /// <summary>
    /// <c>bulkchange</c> с обоими полями: разрешённый список <c>issues</c> не оправдывает
    /// поле <c>query</c> — набор задач по запросу определяет сервер.
    /// </summary>
    [Test]
    public async Task IssueBatch_IssuesPlusQuery_FailsWithPolicyViolation()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var prevIn = Console.In;
        Console.SetIn(new StringReader("""{"issues":["DEV-1"],"query":"Queue: OPS","values":{}}"""));
        try
        {
            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(new[] { "issue", "batch", "--json-stdin" }, sw, er);

            await Assert.That(exit).IsEqualTo(10);
            await Assert.That(inner.Seen).IsEmpty();
            using var doc = JsonDocument.Parse(er.ToString());
            await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
                .IsEqualTo("policy_violation");
        }
        finally
        {
            Console.SetIn(prevIn);
        }
    }

    /// <summary>
    /// <c>link list</c> для разрешённой задачи не отдаёт связанные задачи чужих очередей:
    /// запрос допустим, но тело ответа перечисляет ключи и темы связанных задач.
    /// </summary>
    [Test]
    public async Task LinkList_FiltersOutForeignQueues()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json(
            """
            [{"id":1,"object":{"key":"QA-2","display":"ok"}},
             {"id":2,"object":{"key":"OPS-7","display":"secret"}},
             {"id":3,"object":{"display":"no key"}}]
            """));
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "link", "list", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(sw.ToString()).DoesNotContain("OPS-7");
        await Assert.That(sw.ToString()).DoesNotContain("secret");
        using var doc = JsonDocument.Parse(sw.ToString());
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToArray();
        await Assert.That(ids).IsEquivalentTo(new[] { 1 });
    }

    /// <summary>
    /// Регрессия: без ограничения <c>link list</c> отдаёт ответ как есть.
    /// </summary>
    [Test]
    public async Task LinkList_Unrestricted_ReturnsEverything()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""[{"id":1,"object":{"key":"OPS-7"}}]"""));
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "link", "list", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(sw.ToString()).Contains("OPS-7");
    }

    /// <summary>
    /// <c>yt auth status</c> показывает действующие политики профиля.
    /// </summary>
    [Test]
    public async Task AuthStatus_ShowsReadOnlyAndAllowedQueues()
    {
        using var env = new TestEnv();
        env.SetConfig(
            """
            {
              "default_profile":"ci",
              "profiles":{"ci":{"org_type":"cloud","org_id":"o","read_only":true,
                                "allowed_queues":["DEV","QA"],
                                "auth":{"type":"oauth","token":"y0_X"}}}
            }
            """);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "auth", "status" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("read_only").GetBoolean()).IsTrue();
        var queues = doc.RootElement.GetProperty("allowed_queues")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        await Assert.That(queues).IsEquivalentTo(new[] { "DEV", "QA" });
    }

    /// <summary>
    /// Без ограничения <c>auth status</c> печатает пустой список.
    /// </summary>
    [Test]
    public async Task AuthStatus_NoRestriction_PrintsEmptyAllowedQueues()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "auth", "status" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("allowed_queues").GetArrayLength()).IsEqualTo(0);
    }
}
