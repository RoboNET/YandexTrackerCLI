using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Field;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt field get &lt;id&gt;</c>: без <c>--queue</c>
/// выполняет <c>GET /v3/fields/{id}</c>; с <c>--queue &lt;key&gt;</c> —
/// <c>GET /v3/queues/{key}/localFields/{id}</c>. Мутируют глобальное state
/// (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class FieldGetCommandTests
{
    /// <summary>
    /// Без <c>--queue</c>: URL заканчивается на <c>/fields/summary</c>,
    /// метод GET, stdout — сырой JSON, exit 0.
    /// </summary>
    [Test]
    public async Task Get_WithoutQueue_HitsGlobalFieldByIdPath()
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
                """{"id":"summary","name":"Тема"}""",
                Encoding.UTF8,
                "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "field", "get", "summary" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Get);
        await Assert.That(capturedPath!.EndsWith("/fields/summary", StringComparison.Ordinal)).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("id").GetString()).IsEqualTo("summary");
    }

    /// <summary>
    /// С <c>--queue DEV</c>: URL заканчивается на <c>/queues/DEV/localFields/custom</c>,
    /// метод GET, exit 0.
    /// </summary>
    [Test]
    public async Task Get_WithQueue_HitsLocalFieldByIdPath()
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
                """{"id":"custom","name":"Custom field"}""",
                Encoding.UTF8,
                "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "field", "get", "custom", "--queue", "DEV" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Get);
        await Assert.That(capturedPath!.EndsWith("/queues/DEV/localFields/custom", StringComparison.Ordinal)).IsTrue();
    }
}
