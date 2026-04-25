using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Checklist;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt checklist get &lt;issue-key&gt;</c>: простой GET на
/// <c>/v3/issues/{key}/checklistItems</c>, вывод JSON-массива на stdout.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ChecklistGetCommandTests
{
    /// <summary>
    /// Сервер возвращает массив пунктов — команда печатает его в stdout, exit 0,
    /// request ушёл GET'ом по корректному пути.
    /// </summary>
    [Test]
    public async Task ChecklistGet_ReturnsArray_WritesStdout()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        HttpMethod? method = null;
        string? path = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            method = req.Method;
            path = req.RequestUri!.AbsolutePath;
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"id":"i1","text":"one","checked":false},{"id":"i2","text":"two","checked":true}]""",
                    Encoding.UTF8,
                    "application/json"),
            };
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "checklist", "get", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(method).IsEqualTo(HttpMethod.Get);
        await Assert.That(path!.EndsWith("/issues/DEV-1/checklistItems", StringComparison.Ordinal)).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToArray();
        await Assert.That(ids).IsEquivalentTo(new[] { "i1", "i2" });
    }
}
