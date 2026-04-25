using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.User;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt user get &lt;id&gt;</c>: успешное получение
/// пользователя и обработка <c>404 Not Found</c>. Мутируют глобальное state
/// (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class UserGetCommandTests
{
    /// <summary>
    /// Успешный <c>200 OK</c>: exit 0, stdout содержит сырой JSON, URL заканчивается
    /// на <c>/users/me</c>.
    /// </summary>
    [Test]
    public async Task Get_ByLogin_ReturnsRawJson()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent(
                """{"login":"me","uid":42}""",
                Encoding.UTF8,
                "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "user", "get", "me" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("login").GetString()).IsEqualTo("me");
        await Assert.That(doc.RootElement.GetProperty("uid").GetInt32()).IsEqualTo(42);

        var req = inner.Seen.Single();
        await Assert.That(req.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(req.RequestUri!.AbsolutePath.EndsWith("/users/me", StringComparison.Ordinal)).IsTrue();
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
        var exit = await env.Invoke(new[] { "user", "get", "ghost" }, sw, er);

        await Assert.That(exit).IsEqualTo(5);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString()).IsEqualTo("not_found");
    }
}
