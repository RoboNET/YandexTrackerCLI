using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands;

using System.Net;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// Тесты политики <c>allowed_queues</c> для компонентов и версий: очередь у них
/// либо приезжает в теле запроса (<c>create</c>), либо не приезжает вовсе — ресурс
/// адресуется идентификатором (<c>get</c>/<c>update</c>/<c>delete</c>). В обоих случаях
/// HTTP-guard бессилен, проверка живёт в слое команды.
/// Мутируют глобальный state, поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ComponentVersionQueuePolicyTests
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

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static async Task<(int Exit, string Stdout, string Stderr)> Run(
        TestEnv env,
        params string[] args)
    {
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(args, sw, er);
        return (exit, sw.ToString(), er.ToString());
    }

    /// <summary>
    /// <c>component create --queue OPS</c> — очередь в теле запроса, отказ по политике.
    /// </summary>
    [Test]
    [Arguments("component")]
    [Arguments("version")]
    public async Task Create_TypedQueueFlag_ForbiddenQueue_FailsWithPolicyViolation(string group)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var (exit, _, stderr) = await Run(env, group, "create", "--queue", "OPS", "--name", "n");

        await Assert.That(exit).IsEqualTo(10);
        await Assert.That(inner.Seen).IsEmpty();
        using var doc = JsonDocument.Parse(stderr);
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("policy_violation");
    }

    /// <summary>
    /// То же самое через <c>--json-stdin</c>: проверяется фактическое тело, а не флаги.
    /// </summary>
    [Test]
    [Arguments("component")]
    [Arguments("version")]
    public async Task Create_JsonStdin_ForbiddenQueue_FailsWithPolicyViolation(string group)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var prevIn = Console.In;
        Console.SetIn(new StringReader("""{"queue":{"key":"OPS"},"name":"n"}"""));
        try
        {
            var (exit, _, _) = await Run(env, group, "create", "--json-stdin");
            await Assert.That(exit).IsEqualTo(10);
            await Assert.That(inner.Seen).IsEmpty();
        }
        finally
        {
            Console.SetIn(prevIn);
        }
    }

    /// <summary>
    /// Разрешённая очередь в теле — запрос уходит.
    /// </summary>
    [Test]
    [Arguments("component")]
    [Arguments("version")]
    public async Task Create_AllowedQueue_Succeeds(string group)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"id":"1"}"""));
        env.InnerHandler = inner;

        var (exit, _, _) = await Run(env, group, "create", "--queue", "DEV", "--name", "n");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    /// <summary>
    /// <c>get</c> по идентификатору: владелец резолвится доспросом; чужая очередь — отказ,
    /// сама операция до сервера не доходит (единственный запрос — доспрос).
    /// </summary>
    [Test]
    [Arguments("component", "components")]
    [Arguments("version", "versions")]
    public async Task Get_ById_ForeignQueue_FailsWithPolicyViolation(string group, string resource)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"id":"7","queue":{"key":"OPS"}}"""));
        env.InnerHandler = inner;

        var (exit, _, stderr) = await Run(env, group, "get", "7");

        await Assert.That(exit).IsEqualTo(10);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        await Assert.That(inner.Seen[0].RequestUri!.AbsolutePath).EndsWith($"/{resource}/7");
        using var doc = JsonDocument.Parse(stderr);
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("policy_violation");
    }

    /// <summary>
    /// Своя очередь — операция проходит, доспрос переиспользуется как ответ команды
    /// (второго запроса нет).
    /// </summary>
    [Test]
    [Arguments("component")]
    [Arguments("version")]
    public async Task Get_ById_OwnQueue_Succeeds(string group)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"id":"7","queue":{"key":"DEV"}}"""));
        env.InnerHandler = inner;

        var (exit, stdout, _) = await Run(env, group, "get", "7");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        using var doc = JsonDocument.Parse(stdout);
        await Assert.That(doc.RootElement.GetProperty("id").GetString()).IsEqualTo("7");
    }

    /// <summary>
    /// Отсутствующий ресурс остаётся <c>not_found</c> (exit 5): политика не подменяет
    /// собой отсутствие ресурса.
    /// </summary>
    [Test]
    [Arguments("component")]
    [Arguments("version")]
    public async Task Get_ById_NotFound_StaysNotFound(string group)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"errors":{}}""", HttpStatusCode.NotFound));
        env.InnerHandler = inner;

        var (exit, _, stderr) = await Run(env, group, "get", "404");

        await Assert.That(exit).IsEqualTo(5);
        using var doc = JsonDocument.Parse(stderr);
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("not_found");
    }

    /// <summary>
    /// Ответ без разбираемой очереди — отказ по политике (fail closed): принадлежность
    /// разрешённой очереди доказать не удалось.
    /// </summary>
    [Test]
    [Arguments("component")]
    [Arguments("version")]
    public async Task Get_ById_ResponseWithoutQueue_FailsClosed(string group)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"id":"7"}"""));
        env.InnerHandler = inner;

        var (exit, _, stderr) = await Run(env, group, "get", "7");

        await Assert.That(exit).IsEqualTo(10);
        using var doc = JsonDocument.Parse(stderr);
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("policy_violation");
    }

    /// <summary>
    /// <c>delete</c> чужого ресурса блокируется до отправки DELETE.
    /// </summary>
    [Test]
    [Arguments("component")]
    [Arguments("version")]
    public async Task Delete_ById_ForeignQueue_FailsWithPolicyViolation(string group)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"id":"7","queue":{"key":"OPS"}}"""));
        env.InnerHandler = inner;

        var (exit, _, _) = await Run(env, group, "delete", "7");

        await Assert.That(exit).IsEqualTo(10);
        // Ушёл только доспрос, DELETE — нет.
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        await Assert.That(inner.Seen[0].Method).IsEqualTo(HttpMethod.Get);
    }

    /// <summary>
    /// <c>delete</c> своего ресурса проходит: доспрос + сам DELETE.
    /// </summary>
    [Test]
    [Arguments("component")]
    [Arguments("version")]
    public async Task Delete_ById_OwnQueue_Succeeds(string group)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"id":"7","queue":{"key":"QA"}}"""))
             .Push(_ => new HttpResponseMessage(HttpStatusCode.NoContent)
             {
                 Content = new StringContent(string.Empty, Encoding.UTF8, "application/json"),
             });
        env.InnerHandler = inner;

        var (exit, _, _) = await Run(env, group, "delete", "7");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(2);
        await Assert.That(inner.Seen[1].Method).IsEqualTo(HttpMethod.Delete);
    }

    /// <summary>
    /// <c>update</c> чужого ресурса блокируется до отправки PATCH.
    /// </summary>
    [Test]
    [Arguments("component")]
    [Arguments("version")]
    public async Task Update_ById_ForeignQueue_FailsWithPolicyViolation(string group)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"id":"7","queue":{"key":"OPS"}}"""));
        env.InnerHandler = inner;

        var (exit, _, _) = await Run(env, group, "update", "7", "--name", "n2");

        await Assert.That(exit).IsEqualTo(10);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        await Assert.That(inner.Seen[0].Method).IsEqualTo(HttpMethod.Get);
    }

    /// <summary>
    /// Регрессия: без ограничения профиля доспроса не происходит вовсе — лишнего
    /// round-trip'а в обычном режиме нет.
    /// </summary>
    [Test]
    [Arguments("component")]
    [Arguments("version")]
    public async Task Get_ById_Unrestricted_DoesNotResolveOwner(string group)
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json("""{"id":"7","queue":{"key":"OPS"}}"""));
        env.InnerHandler = inner;

        var (exit, _, _) = await Run(env, group, "get", "7");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }
}
