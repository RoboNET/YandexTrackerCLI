using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Comment;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt comment list &lt;issue-key&gt;</c>: пагинация
/// (сборка элементов со всех страниц) и ограничение количества через <c>--max</c>.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class CommentListCommandTests
{
    /// <summary>
    /// Команда корректно склеивает элементы из нескольких страниц в единый JSON-массив
    /// и запрашивает путь <c>/issues/{key}/comments</c>.
    /// </summary>
    [Test]
    public async Task CommentList_ConcatenatesAllPages()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "2");
            r.Content = new StringContent("""[{"id":"1"},{"id":"2"}]""", Encoding.UTF8, "application/json");
            return r;
        }).Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "2");
            r.Content = new StringContent("""[{"id":"3"}]""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "list", "DEV-1" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToArray();
        await Assert.That(ids).IsEquivalentTo(new[] { "1", "2", "3" });
        await Assert.That(inner.Seen[0].RequestUri!.AbsolutePath.EndsWith("/issues/DEV-1/comments")).IsTrue();
    }

    /// <summary>
    /// При <c>--format table</c> комментарии рендерятся через <see cref="Output.CommentBlockRenderer"/>:
    /// stdout содержит автора, дату и markdown-тело каждого комментария, а не JSON-массив.
    /// </summary>
    [Test]
    public async Task CommentList_TableFormat_RendersBlocks_WithAuthorAndText()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        env.Set("YT_PAGER", "cat");

        env.InnerHandler = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "1");
            r.Content = new StringContent(
                """[{"id":"1","createdAt":"2025-12-10T10:00:00Z","createdBy":{"display":"Alice"},"text":"first comment"}]""",
                Encoding.UTF8,
                "application/json");
            return r;
        });

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "--format", "table", "comment", "list", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        var output = sw.ToString();
        await Assert.That(output).Contains("Alice");
        await Assert.That(output).Contains("first comment");
        await Assert.That(output).Contains("2025-12-10");
        // Не JSON-массив.
        await Assert.That(output.TrimStart().StartsWith('[')).IsFalse();
    }

    /// <summary>
    /// Опция <c>--max</c> ограничивает количество элементов, попадающих в вывод.
    /// </summary>
    [Test]
    public async Task CommentList_WithMax_StopsAtLimit()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "2");
            r.Content = new StringContent("""[{"id":"1"},{"id":"2"},{"id":"3"}]""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "list", "DEV-1", "--max", "2" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToArray();
        await Assert.That(ids).IsEquivalentTo(new[] { "1", "2" });
    }
}
