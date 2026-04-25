using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Version;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt version get &lt;id&gt;</c>: успешное получение
/// версии и обработка <c>404 Not Found</c>. Мутируют глобальное state
/// (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class VersionGetCommandTests
{
    /// <summary>
    /// Успешный <c>200 OK</c>: exit 0, stdout содержит сырой JSON, URL заканчивается
    /// на <c>/versions/7</c>.
    /// </summary>
    [Test]
    public async Task Get_ById_ReturnsRawJson()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent(
                """{"id":7,"name":"v1.0"}""",
                Encoding.UTF8,
                "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "version", "get", "7" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("id").GetInt32()).IsEqualTo(7);
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("v1.0");
        await Assert.That(inner.Seen.Single().RequestUri!.AbsolutePath.EndsWith("/versions/7")).IsTrue();
    }

    /// <summary>
    /// Ответ <c>404 Not Found</c> → exit 5, stderr содержит структурированный JSON
    /// с <c>error.code == "not_found"</c>.
    /// </summary>
    [Test]
    public async Task Get_404_ReturnsNotFound_Exit5()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        env.InnerHandler = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "version", "get", "9999" }, sw, er);

        await Assert.That(exit).IsEqualTo(5);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString()).IsEqualTo("not_found");
    }
}
