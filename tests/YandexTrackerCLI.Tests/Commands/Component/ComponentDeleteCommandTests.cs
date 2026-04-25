using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Component;

using System.Net;
using System.Net.Http;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt component delete &lt;id&gt;</c>: ответ <c>204 No Content</c>
/// → success-маркер <c>{"deleted":"&lt;id&gt;"}</c>, и обработка <c>404 Not Found</c>.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ComponentDeleteCommandTests
{
    /// <summary>
    /// <c>204 No Content</c>: exit 0, stdout содержит <c>{"deleted":"42"}</c>, запрос
    /// ушёл методом DELETE на <c>/components/42</c>.
    /// </summary>
    [Test]
    public async Task Delete_204_WritesDeletedMarker()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        HttpMethod? capturedMethod = null;
        string? capturedPath = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedMethod = req.Method;
            capturedPath = req.RequestUri!.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "component", "delete", "42" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Delete);
        await Assert.That(capturedPath!.EndsWith("/components/42", StringComparison.Ordinal)).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("deleted").GetString()).IsEqualTo("42");
    }

    /// <summary>
    /// <c>404 Not Found</c>: exit 5, stderr содержит <c>error.code == "not_found"</c>.
    /// </summary>
    [Test]
    public async Task Delete_404_ReturnsNotFound_Exit5()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        env.InnerHandler = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "component", "delete", "9999" }, sw, er);

        await Assert.That(exit).IsEqualTo(5);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString()).IsEqualTo("not_found");
    }
}
