using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Issue;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt issue find --yql "..."</c>: постраничный
/// <c>POST /v3/issues/_search</c>, склейка страниц в JSON-массив, режим <c>--stream</c>
/// (NDJSON) и ограничение <c>--max</c>. Мутируют глобальное state, поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class IssueFindCommandTests
{
    /// <summary>
    /// По умолчанию (без <c>--stream</c>) команда конкатенирует элементы всех страниц
    /// в один JSON-массив и отправляет YQL в теле запроса как <c>{"query":"..."}</c>.
    /// </summary>
    [Test]
    public async Task Find_DefaultArrayMode_ConcatenatesPages_AndPostsYqlBody()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var capturedBodies = new List<string>();
        var inner = new TestHttpMessageHandler();
        inner.Push(req =>
        {
            // Считываем тело до диспоуза HttpRequestMessage в TrackerClient (using var).
            capturedBodies.Add(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "2");
            r.Content = new StringContent("""[{"key":"DEV-1"},{"key":"DEV-2"}]""", Encoding.UTF8, "application/json");
            return r;
        }).Push(req =>
        {
            capturedBodies.Add(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "2");
            r.Content = new StringContent("""[{"key":"DEV-3"}]""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "find", "--yql", "Queue: DEV" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        var keys = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("key").GetString()!).ToArray();
        await Assert.That(keys).IsEquivalentTo(new[] { "DEV-1", "DEV-2", "DEV-3" });

        var first = inner.Seen[0];
        await Assert.That(first.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(first.RequestUri!.AbsolutePath.EndsWith("/issues/_search")).IsTrue();
        using var bodyDoc = JsonDocument.Parse(capturedBodies[0]);
        await Assert.That(bodyDoc.RootElement.GetProperty("query").GetString()).IsEqualTo("Queue: DEV");
    }

    /// <summary>
    /// Опция <c>--max</c> обрывает чтение до исчерпания страниц: когда лимит достигнут,
    /// новые страницы не запрашиваются.
    /// </summary>
    [Test]
    public async Task Find_MaxLimit_StopsEarly()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "5");
            r.Content = new StringContent(
                """[{"key":"DEV-1"},{"key":"DEV-2"},{"key":"DEV-3"}]""",
                Encoding.UTF8,
                "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "find", "--yql", "q", "--max", "2" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetArrayLength()).IsEqualTo(2);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    /// <summary>
    /// Режим <c>--stream</c> печатает по одному JSON-объекту на строку (NDJSON), без внешнего массива.
    /// </summary>
    [Test]
    public async Task Find_Stream_EmitsNdjson_OneLinePerObject()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "1");
            r.Content = new StringContent(
                """[{"key":"DEV-1"},{"key":"DEV-2"}]""",
                Encoding.UTF8,
                "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "find", "--yql", "q", "--stream" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        await Assert.That(lines.Length).IsEqualTo(2);
        using var d1 = JsonDocument.Parse(lines[0]);
        using var d2 = JsonDocument.Parse(lines[1]);
        await Assert.That(d1.RootElement.GetProperty("key").GetString()).IsEqualTo("DEV-1");
        await Assert.That(d2.RootElement.GetProperty("key").GetString()).IsEqualTo("DEV-2");
    }

    /// <summary>
    /// Когда не указан ни <c>--yql</c>, ни simple-фильтр, команда отклоняет запрос
    /// с <c>InvalidArgs</c> (exit=2) и пишет JSON в stderr без обращения к HTTP-слою.
    /// </summary>
    [Test]
    public async Task Find_NoFilters_Returns_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "find" }, sw, er);
        await Assert.That(exit).IsEqualTo(2);
        await Assert.That(er.ToString()).Contains("invalid_args");
    }

    /// <summary>
    /// Simple-фильтры <c>--queue</c> и <c>--status</c> собираются в YQL-выражение,
    /// которое отправляется в теле запроса как <c>{"query":"..."}</c>.
    /// </summary>
    [Test]
    public async Task Find_WithSimpleFilters_BuildsExpectedYql()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var capturedBodies = new List<string>();
        var inner = new TestHttpMessageHandler();
        inner.Push(req =>
        {
            capturedBodies.Add(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "1");
            r.Content = new StringContent("[]", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "issue", "find", "--queue", "DEV", "--status", "open" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        using var bodyDoc = JsonDocument.Parse(capturedBodies[0]);
        await Assert.That(bodyDoc.RootElement.GetProperty("query").GetString())
            .IsEqualTo("Queue: \"DEV\" AND Status: \"open\"");
    }

    /// <summary>
    /// Одновременное использование <c>--yql</c> и simple-фильтра отклоняется
    /// с <c>InvalidArgs</c> (exit=2) ещё до обращения к HTTP.
    /// </summary>
    [Test]
    public async Task Find_BothYqlAndSimpleFilter_Returns_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "issue", "find", "--yql", "q", "--queue", "DEV" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(2);
        await Assert.That(er.ToString()).Contains("invalid_args");
    }
}
