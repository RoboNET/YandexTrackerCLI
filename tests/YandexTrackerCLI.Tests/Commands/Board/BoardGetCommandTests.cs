using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Board;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt board get &lt;id&gt;</c>: успешное получение
/// доски и обработка <c>404 Not Found</c>.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class BoardGetCommandTests
{
    /// <summary>
    /// Успешный ответ <c>200 OK</c> с JSON — exit 0, stdout содержит сырой JSON из ответа API,
    /// а URL запроса заканчивается на <c>/boards/42</c>.
    /// </summary>
    [Test]
    public async Task Get_ById_ReturnsRawJson()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":42,"name":"Sprint Board"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "board", "get", "42" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("id").GetInt32()).IsEqualTo(42);
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("Sprint Board");
        await Assert.That(inner.Seen.Single().RequestUri!.AbsolutePath.EndsWith("/boards/42")).IsTrue();
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
        var exit = await env.Invoke(new[] { "board", "get", "9999" }, sw, er);

        await Assert.That(exit).IsEqualTo(5);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString()).IsEqualTo("not_found");
    }
}
