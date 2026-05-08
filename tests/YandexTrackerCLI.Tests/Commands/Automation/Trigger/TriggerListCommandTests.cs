namespace YandexTrackerCLI.Tests.Commands.Automation.Trigger;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Tests.Http;

/// <summary>
/// End-to-end тесты команды <c>yt automation trigger list</c>:
/// проверяют корректное построение URL пути к ресурсу триггеров очереди
/// и агрегацию ответа в единый JSON-массив на stdout.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class TriggerListCommandTests
{
    /// <summary>
    /// Список триггеров очереди возвращается одной страницей и печатается как JSON-массив,
    /// при этом запрос идёт по адресу <c>/queues/{queue}/triggers/</c>.
    /// </summary>
    [Test]
    public async Task List_PagedResponse_AggregatesItems()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        string? capturedPath = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedPath = req.RequestUri!.AbsolutePath + "?" + req.RequestUri.Query.TrimStart('?');
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"id":1,"name":"t1"},{"id":2,"name":"t2"}]""",
                    Encoding.UTF8,
                    "application/json"),
            };
            r.Headers.Add("X-Total-Pages", "1");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "automation", "trigger", "list", "--queue", "DEV" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedPath!.Contains("/queues/DEV/triggers/")).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetArrayLength()).IsEqualTo(2);
        await Assert.That(doc.RootElement[0].GetProperty("name").GetString()).IsEqualTo("t1");
    }
}
