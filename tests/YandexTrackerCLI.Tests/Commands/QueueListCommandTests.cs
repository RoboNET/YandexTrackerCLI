using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt queue list</c>: полный маршрут через
/// <see cref="YandexTrackerCLI.Commands.Queue.QueueListCommand"/>, <see cref="TrackerContextFactory"/>,
/// подменённый HTTP-handler (через <see cref="TestEnv.InnerHandler"/>).
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class QueueListCommandTests
{
    /// <summary>
    /// Команда корректно склеивает элементы из нескольких страниц в единый JSON-массив.
    /// </summary>
    [Test]
    public async Task QueueList_ConcatenatesAllPages()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"a","profiles":{"a":{"org_type":"cloud","org_id":"o","read_only":false,"auth":{"type":"oauth","token":"y0_X"}}}}""");

        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "2");
            r.Content = new StringContent("""[{"key":"A"},{"key":"B"}]""", Encoding.UTF8, "application/json");
            return r;
        }).Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "2");
            r.Content = new StringContent("""[{"key":"C"}]""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "queue", "list" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        var keys = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("key").GetString()!).ToArray();
        await Assert.That(keys).IsEquivalentTo(new[] { "A", "B", "C" });
    }

    /// <summary>
    /// Опция <c>--max</c> ограничивает количество элементов, попадающих в вывод.
    /// </summary>
    [Test]
    public async Task QueueList_WithMax_StopsAtLimit()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"a","profiles":{"a":{"org_type":"cloud","org_id":"o","read_only":false,"auth":{"type":"oauth","token":"y0_X"}}}}""");

        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "2");
            r.Content = new StringContent("""[{"key":"A"},{"key":"B"},{"key":"C"}]""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "queue", "list", "--max", "2" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        var keys = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("key").GetString()!).ToArray();
        await Assert.That(keys).IsEquivalentTo(new[] { "A", "B" });
    }
}
