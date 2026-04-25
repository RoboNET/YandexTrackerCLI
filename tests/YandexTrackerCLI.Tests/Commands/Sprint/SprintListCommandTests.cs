using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Sprint;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt sprint list [--board &lt;id&gt;]</c>: выбор пути
/// запроса в зависимости от наличия опции <c>--board</c>, пагинация и ограничение
/// через <c>--max</c> (проверяется косвенно через single-page сценарий).
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class SprintListCommandTests
{
    /// <summary>
    /// Без <c>--board</c> запрашивается <c>/sprints</c>; элементы из нескольких
    /// страниц корректно склеиваются в единый JSON-массив.
    /// </summary>
    [Test]
    public async Task SprintList_WithoutBoard_UsesSprintsPath_AndPages()
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
        var exit = await env.Invoke(new[] { "sprint", "list" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToArray();
        await Assert.That(ids).IsEquivalentTo(new[] { 1, 2, 3 });
        await Assert.That(inner.Seen[0].RequestUri!.AbsolutePath.EndsWith("/sprints")).IsTrue();
    }

    /// <summary>
    /// С <c>--board 42</c> запрашивается <c>/boards/42/sprints</c>; опция <c>--max</c>
    /// ограничивает количество элементов в выводе.
    /// </summary>
    [Test]
    public async Task SprintList_WithBoard_UsesBoardSprintsPath_AndMax()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""[{"id":10},{"id":20},{"id":30}]""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "sprint", "list", "--board", "42", "--max", "2" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToArray();
        await Assert.That(ids).IsEquivalentTo(new[] { 10, 20 });
        await Assert.That(inner.Seen[0].RequestUri!.AbsolutePath.EndsWith("/boards/42/sprints")).IsTrue();
    }
}
