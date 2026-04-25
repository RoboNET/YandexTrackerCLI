using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Link;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt link list &lt;issue-key&gt;</c>: простой GET на
/// <c>/v3/issues/{key}/links</c>, вывод JSON-массива на stdout.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class LinkListCommandTests
{
    /// <summary>
    /// Сервер возвращает массив связей — команда печатает его в stdout, exit 0,
    /// request ушёл GET'ом по корректному пути.
    /// </summary>
    [Test]
    public async Task LinkList_ReturnsArray_WritesStdout()
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
                    """[{"id":1,"type":{"id":"relates"},"object":{"key":"DEV-2"}},{"id":2,"type":{"id":"depends-on"},"object":{"key":"DEV-3"}}]""",
                    Encoding.UTF8,
                    "application/json"),
            };
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "link", "list", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(method).IsEqualTo(HttpMethod.Get);
        await Assert.That(path!.EndsWith("/issues/DEV-1/links", StringComparison.Ordinal)).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToArray();
        await Assert.That(ids).IsEquivalentTo(new[] { 1, 2 });
    }
}
