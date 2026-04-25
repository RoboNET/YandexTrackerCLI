using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Issue;

using System.Net;
using System.Net.Http;
using System.Text;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt issue transition &lt;key&gt;</c>: режимы <c>--list</c>
/// (GET) и <c>--to</c> (POST /_execute) с опциональным raw JSON-телом и отказ при отсутствии
/// обязательных флагов. Мутируют глобальное state (env + Console + AsyncLocal), поэтому
/// выполняются последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class IssueTransitionCommandTests
{
    /// <summary>
    /// <c>--to &lt;id&gt;</c> без body-флагов — уходит <c>POST /v3/issues/DEV-1/transitions/close/_execute</c>
    /// с телом по умолчанию <c>{}</c>. Exit = 0.
    /// </summary>
    [Test]
    public async Task Transition_To_PostsExecute_WithEmptyBody()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        HttpMethod? method = null;
        string? path = null;
        string? body = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            method = req.Method;
            path = req.RequestUri!.AbsolutePath;
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("[]", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "transition", "DEV-1", "--to", "close" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(method).IsEqualTo(HttpMethod.Post);
        await Assert.That(path!.EndsWith("/issues/DEV-1/transitions/close/_execute", StringComparison.Ordinal)).IsTrue();
        await Assert.That(body).IsEqualTo("{}");
    }

    /// <summary>
    /// <c>--list</c> — уходит <c>GET /v3/issues/DEV-1/transitions</c>. Exit = 0.
    /// </summary>
    [Test]
    public async Task Transition_List_GetsTransitions()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        HttpMethod? method = null;
        string? path = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            method = req.Method;
            path = req.RequestUri!.AbsolutePath;
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""[{"id":"close","display":"Close"}]""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "transition", "DEV-1", "--list" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(method).IsEqualTo(HttpMethod.Get);
        await Assert.That(path!.EndsWith("/issues/DEV-1/transitions", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// <c>--to &lt;id&gt; --json-file</c> — содержимое файла уходит в тело запроса без трансформаций.
    /// </summary>
    [Test]
    public async Task Transition_WithJsonFile_SendsCustomBody()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var filePath = Path.Combine(Path.GetTempPath(), "tr-" + Guid.NewGuid() + ".json");
        var raw = """{"resolution":"fixed","comment":{"text":"done"}}""";
        await File.WriteAllTextAsync(filePath, raw);

        string? body = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "issue", "transition", "DEV-1", "--to", "close", "--json-file", filePath },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(body).IsEqualTo(raw);
    }

    /// <summary>
    /// Без <c>--to</c> и без <c>--list</c> — exit 2 (<see cref="YandexTrackerCLI.Core.Api.Errors.ErrorCode.InvalidArgs"/>).
    /// </summary>
    [Test]
    public async Task Transition_NeitherTo_NorList_Returns_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "transition", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(2);
    }
}
