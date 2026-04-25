using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Sprint;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt sprint get &lt;id&gt;</c>: успешное получение
/// спринта и обработка <c>404 Not Found</c>.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class SprintGetCommandTests
{
    /// <summary>
    /// Успешный ответ <c>200 OK</c> с JSON — exit 0, stdout содержит сырой JSON из ответа API,
    /// а URL запроса заканчивается на <c>/sprints/7</c>.
    /// </summary>
    [Test]
    public async Task Get_ById_ReturnsRawJson()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":7,"name":"Sprint 7"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "sprint", "get", "7" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("id").GetInt32()).IsEqualTo(7);
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("Sprint 7");
        await Assert.That(inner.Seen.Single().RequestUri!.AbsolutePath.EndsWith("/sprints/7")).IsTrue();
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
        var exit = await env.Invoke(new[] { "sprint", "get", "9999" }, sw, er);

        await Assert.That(exit).IsEqualTo(5);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString()).IsEqualTo("not_found");
    }
}
