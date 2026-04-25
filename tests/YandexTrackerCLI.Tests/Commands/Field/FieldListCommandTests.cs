using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Field;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt field list</c>: без <c>--queue</c> выполняет
/// <c>GET /v3/fields</c>; с <c>--queue &lt;key&gt;</c> — <c>GET /v3/queues/{key}/localFields</c>.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class FieldListCommandTests
{
    /// <summary>
    /// Без <c>--queue</c>: URL заканчивается на <c>/fields</c>, метод GET,
    /// stdout — тот же массив, exit 0.
    /// </summary>
    [Test]
    public async Task List_WithoutQueue_HitsGlobalFieldsPath()
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
                """[{"id":"summary"},{"id":"description"}]""",
                Encoding.UTF8,
                "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "field", "list" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Get);
        await Assert.That(capturedPath!.EndsWith("/fields", StringComparison.Ordinal)).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToArray();
        await Assert.That(ids).IsEquivalentTo(new[] { "summary", "description" });
    }

    /// <summary>
    /// С <c>--queue DEV</c>: URL заканчивается на <c>/queues/DEV/localFields</c>,
    /// метод GET, exit 0.
    /// </summary>
    [Test]
    public async Task List_WithQueue_HitsLocalFieldsPath()
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
            r.Content = new StringContent("[]", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "field", "list", "--queue", "DEV" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Get);
        await Assert.That(capturedPath!.EndsWith("/queues/DEV/localFields", StringComparison.Ordinal)).IsTrue();
    }
}
