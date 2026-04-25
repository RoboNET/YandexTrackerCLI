using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Board;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt board list</c>: пагинация (склейка элементов
/// со всех страниц) и ограничение количества через <c>--max</c>.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class BoardListCommandTests
{
    /// <summary>
    /// Команда корректно склеивает элементы из нескольких страниц в единый JSON-массив
    /// и запрашивает путь <c>/boards</c>.
    /// </summary>
    [Test]
    public async Task BoardList_ConcatenatesAllPages()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "2");
            r.Content = new StringContent("""[{"id":1},{"id":2}]""", Encoding.UTF8, "application/json");
            return r;
        }).Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "2");
            r.Content = new StringContent("""[{"id":3}]""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "board", "list" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToArray();
        await Assert.That(ids).IsEquivalentTo(new[] { 1, 2, 3 });
        await Assert.That(inner.Seen[0].RequestUri!.AbsolutePath.EndsWith("/boards")).IsTrue();
    }

    /// <summary>
    /// Опция <c>--max</c> ограничивает количество элементов, попадающих в вывод.
    /// </summary>
    [Test]
    public async Task BoardList_WithMax_StopsAtLimit()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "2");
            r.Content = new StringContent("""[{"id":1},{"id":2},{"id":3}]""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "board", "list", "--max", "2" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToArray();
        await Assert.That(ids).IsEquivalentTo(new[] { 1, 2 });
    }
}
