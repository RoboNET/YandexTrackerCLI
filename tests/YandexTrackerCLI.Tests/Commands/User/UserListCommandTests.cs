using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.User;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt user list</c>: полный маршрут через
/// <see cref="YandexTrackerCLI.Commands.User.UserListCommand"/>, <see cref="TrackerContextFactory"/>,
/// подменённый HTTP-handler (через <see cref="TestEnv.InnerHandler"/>).
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class UserListCommandTests
{
    /// <summary>
    /// Команда корректно склеивает элементы из нескольких страниц в единый JSON-массив.
    /// </summary>
    [Test]
    public async Task UserList_ConcatenatesAllPages()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "2");
            r.Content = new StringContent("""[{"login":"a"},{"login":"b"}]""", Encoding.UTF8, "application/json");
            return r;
        }).Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "2");
            r.Content = new StringContent("""[{"login":"c"}]""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "user", "list" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        var logins = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("login").GetString()!).ToArray();
        await Assert.That(logins).IsEquivalentTo(new[] { "a", "b", "c" });
    }

    /// <summary>
    /// Опция <c>--max</c> ограничивает количество элементов, попадающих в вывод.
    /// </summary>
    [Test]
    public async Task UserList_WithMax_StopsAtLimit()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "2");
            r.Content = new StringContent("""[{"login":"a"},{"login":"b"},{"login":"c"}]""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "user", "list", "--max", "2" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        var logins = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("login").GetString()!).ToArray();
        await Assert.That(logins).IsEquivalentTo(new[] { "a", "b" });
    }
}
