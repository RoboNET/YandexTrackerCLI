using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Issue;

using System.Net;
using System.Net.Http;
using System.Text;
using TUnit.Core;
using YandexTrackerCLI.Core.Api.Errors;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt issue changelog &lt;key&gt;</c>: GET-выгрузка истории
/// изменений задачи в агрегированном (JSON-массив) и потоковом (NDJSON) режимах.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому выполняются последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class IssueChangelogCommandTests
{
    /// <summary>
    /// <c>changelog &lt;key&gt;</c> — уходит <c>GET /v3/issues/DEV-1/changelog</c>,
    /// результат выводится как распарсенный JSON-массив. Exit = 0.
    /// </summary>
    [Test]
    public async Task Changelog_GetsChangelog_AsJsonArray()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        HttpMethod? method = null;
        string? path = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            method = req.Method;
            path = req.RequestUri!.AbsolutePath;
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""[{"id":"1","fields":[]}]""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "changelog", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(method).IsEqualTo(HttpMethod.Get);
        await Assert.That(path!.EndsWith("/issues/DEV-1/changelog", StringComparison.Ordinal)).IsTrue();
        await Assert.That(sw.ToString().Contains("\"id\"", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// <c>changelog &lt;key&gt; --stream</c> — выводит NDJSON: каждая запись на отдельной строке. Exit = 0.
    /// </summary>
    [Test]
    public async Task Changelog_Stream_EmitsNdjson()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler().Push(req =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent(
                """[{"id":"1","fields":[]},{"id":"2","fields":[]}]""",
                Encoding.UTF8,
                "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "changelog", "DEV-1", "--stream" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        var lines = sw.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        await Assert.That(lines.Length).IsEqualTo(2);
        await Assert.That(lines[0].Contains("\"id\":\"1\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(lines[1].Contains("\"id\":\"2\"", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// Курсорная пагинация: первый ответ несёт <c>Link: ...; rel="next"</c>, второй — нет.
    /// Команда делает два запроса и склеивает обе страницы в один JSON-массив. Exit = 0.
    /// </summary>
    [Test]
    public async Task Changelog_CursorPagination_FollowsLinkNext()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""[{"id":"1"}]""", Encoding.UTF8, "application/json");
            r.Headers.TryAddWithoutValidation(
                "Link",
                """<https://api.tracker.yandex.net/v3/issues/DEV-1/changelog?id=1&perPage=100>; rel="next" """);
            return r;
        });
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""[{"id":"2"}]""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "changelog", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(2);
        // Вторая страница запрошена по курсору из Link-заголовка.
        await Assert.That(inner.Seen[1].RequestUri!.Query.Contains("id=1", StringComparison.Ordinal)).IsTrue();
        var output = sw.ToString();
        await Assert.That(output.Contains("\"id\":\"1\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(output.Contains("\"id\":\"2\"", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// <c>--max 2</c> ограничивает вывод первыми двумя записями из трёх. Exit = 0.
    /// </summary>
    [Test]
    public async Task Changelog_Max_TruncatesOutput()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent(
                """[{"id":"1"},{"id":"2"},{"id":"3"}]""",
                Encoding.UTF8,
                "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "changelog", "DEV-1", "--stream", "--max", "2" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        var lines = sw.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        await Assert.That(lines.Length).IsEqualTo(2);
        await Assert.That(lines[0].Contains("\"id\":\"1\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(lines[1].Contains("\"id\":\"2\"", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// Пустой changelog (пустой массив) — exit 0, вывод <c>[]</c>.
    /// </summary>
    [Test]
    public async Task Changelog_Empty_OutputsEmptyArray()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("[]", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "changelog", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(sw.ToString().Trim()).IsEqualTo("[]");
    }

    /// <summary>
    /// Ответ 404 — exit равен <see cref="YandexTrackerCLI.Core.Api.Errors.ErrorCode.NotFound"/>.ToExitCode() (= 5).
    /// </summary>
    [Test]
    public async Task Changelog_NotFound_ReturnsNotFoundExitCode()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.NotFound);
            r.Content = new StringContent("not found", Encoding.UTF8, "text/plain");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "changelog", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(YandexTrackerCLI.Core.Api.Errors.ErrorCode.NotFound.ToExitCode());
        await Assert.That(exit).IsEqualTo(5);
    }
}
