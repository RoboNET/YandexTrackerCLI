using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Issue;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt issue get &lt;key&gt;</c>: полный маршрут через
/// <see cref="YandexTrackerCLI.Commands.Issue.IssueGetCommand"/>, <see cref="TrackerContextFactory"/>
/// и подменённый HTTP-handler (через <see cref="TestEnv.InnerHandler"/>).
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class IssueGetCommandTests
{
    /// <summary>
    /// Успешный ответ <c>200 OK</c> с JSON — exit 0, stdout содержит сырой JSON из ответа API,
    /// а URL запроса заканчивается на <c>/issues/DEV-1</c>.
    /// </summary>
    [Test]
    public async Task Get_ByKey_ReturnsRawJson()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"a","profiles":{"a":{"org_type":"cloud","org_id":"o","read_only":false,"auth":{"type":"oauth","token":"y0_X"}}}}""");

        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"key":"DEV-1","summary":"bug"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "get", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("key").GetString()).IsEqualTo("DEV-1");
        await Assert.That(doc.RootElement.GetProperty("summary").GetString()).IsEqualTo("bug");
        await Assert.That(inner.Seen.Single().RequestUri!.AbsolutePath.EndsWith("/issues/DEV-1")).IsTrue();
    }

    /// <summary>
    /// Ответ <c>404 Not Found</c> → exit 5, stderr содержит структурированный JSON
    /// с <c>error.code == "not_found"</c>.
    /// </summary>
    [Test]
    public async Task Get_404_ReturnsNotFound_Exit5()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"a","profiles":{"a":{"org_type":"cloud","org_id":"o","read_only":false,"auth":{"type":"oauth","token":"y0_X"}}}}""");
        env.InnerHandler = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "get", "DEV-NOPE" }, sw, er);

        await Assert.That(exit).IsEqualTo(5);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString()).IsEqualTo("not_found");
    }

    /// <summary>
    /// При <c>--format table</c> issue выводится через <see cref="IssueDetailRenderer"/>:
    /// stdout содержит summary, метаданные (Queue/Created/Assignee/Tags) и хедер с ключом задачи.
    /// </summary>
    [Test]
    public async Task Get_TableFormat_RendersDetailView_WithSummaryAndMetadata()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"a","profiles":{"a":{"org_type":"cloud","org_id":"o","read_only":false,"auth":{"type":"oauth","token":"y0_X"}}}}""");
        // Disable pager via env so table-format не пытается запустить less в тесте.
        env.Set("YT_PAGER", "cat");

        env.InnerHandler = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent(
                """{"key":"DEV-1","summary":"Fix bug","queue":{"display":"Dev","key":"DEV"},"description":"Steps to reproduce","tags":["urgent"]}""",
                Encoding.UTF8,
                "application/json");
            return r;
        });

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "--format", "table", "issue", "get", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        var output = sw.ToString();
        await Assert.That(output).Contains("DEV-1");
        await Assert.That(output).Contains("Fix bug");
        await Assert.That(output).Contains("Queue");
        await Assert.That(output).Contains("Dev (DEV)");
        await Assert.That(output).Contains("Description");
        await Assert.That(output).Contains("Steps to reproduce");
        await Assert.That(output).Contains("urgent");
    }

    /// <summary>
    /// Ключ содержит слэш — слэш должен быть URL-encoded в пути запроса (<c>WEIRD%2FKEY</c>).
    /// </summary>
    [Test]
    public async Task Get_UrlEncodesKeyWithSlashes()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"a","profiles":{"a":{"org_type":"cloud","org_id":"o","read_only":false,"auth":{"type":"oauth","token":"y0_X"}}}}""");
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"key":"WEIRD/KEY"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "get", "WEIRD/KEY" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(inner.Seen.Single().RequestUri!.AbsoluteUri).Contains("WEIRD%2FKEY");
    }
}
