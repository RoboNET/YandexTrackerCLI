using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.User;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt user search --query &lt;text&gt;</c>:
/// выполняет <c>GET /v3/users?query=...</c> и печатает ответ как есть.
/// <c>--query</c> — обязательный option. Мутируют глобальное state
/// (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class UserSearchCommandTests
{
    /// <summary>
    /// Успешный <c>200 OK</c> с массивом: exit 0, метод GET, URL содержит
    /// <c>/users</c> и query-параметр <c>query</c>, stdout — тот же массив.
    /// </summary>
    [Test]
    public async Task Search_WithQuery_ReturnsArray()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        HttpMethod? capturedMethod = null;
        string? capturedPath = null;
        string? capturedQuery = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedMethod = req.Method;
            capturedPath = req.RequestUri!.AbsolutePath;
            capturedQuery = req.RequestUri!.Query;
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent(
                """[{"login":"alice","uid":1},{"login":"alicia","uid":2}]""",
                Encoding.UTF8,
                "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "user", "search", "--query", "ali" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Get);
        await Assert.That(capturedPath!.EndsWith("/users", StringComparison.Ordinal)).IsTrue();
        await Assert.That(capturedQuery!.Contains("query=ali", StringComparison.Ordinal)).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        var logins = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("login").GetString()!).ToArray();
        await Assert.That(logins).IsEquivalentTo(new[] { "alice", "alicia" });
    }

    /// <summary>
    /// Без <c>--query</c>: System.CommandLine помечает отсутствие обязательного option
    /// ошибкой парсинга → ненулевой exit, HTTP не вызывается.
    /// </summary>
    [Test]
    public async Task Search_MissingQuery_FailsBeforeHttp()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "user", "search" }, sw, er);

        await Assert.That(exit).IsNotEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }
}
