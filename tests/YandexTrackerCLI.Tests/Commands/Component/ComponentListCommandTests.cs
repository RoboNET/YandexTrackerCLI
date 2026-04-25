using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Component;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt component list --queue &lt;key&gt;</c>:
/// выполняет <c>GET /v3/queues/{queue}/components</c> и печатает ответ как есть.
/// <c>--queue</c> — обязательный option. Мутируют глобальное state
/// (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ComponentListCommandTests
{
    /// <summary>
    /// Успешный <c>200 OK</c> с массивом: exit 0, метод GET, URL заканчивается на
    /// <c>/queues/DEV/components</c>, stdout — тот же массив.
    /// </summary>
    [Test]
    public async Task List_WithQueue_ReturnsArray()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        HttpMethod? capturedMethod = null;
        string? capturedPath = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedMethod = req.Method;
            capturedPath = req.RequestUri!.AbsolutePath;
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent(
                """[{"id":1,"name":"API"},{"id":2,"name":"UI"}]""",
                Encoding.UTF8,
                "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "component", "list", "--queue", "DEV" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Get);
        await Assert.That(capturedPath!.EndsWith("/queues/DEV/components", StringComparison.Ordinal)).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToArray();
        await Assert.That(ids).IsEquivalentTo(new[] { 1, 2 });
    }

    /// <summary>
    /// Без <c>--queue</c>: System.CommandLine помечает отсутствие обязательного option
    /// ошибкой парсинга → ненулевой exit, HTTP не вызывается.
    /// </summary>
    [Test]
    public async Task List_MissingQueue_FailsBeforeHttp()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "component", "list" }, sw, er);

        await Assert.That(exit).IsNotEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }
}
