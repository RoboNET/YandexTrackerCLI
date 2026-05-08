namespace YandexTrackerCLI.Tests.Commands.Automation.Trigger;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Tests.Http;

/// <summary>
/// End-to-end тесты команды <c>yt automation trigger get</c>:
/// проверяют корректное построение URL ресурса триггера и вывод
/// тела ответа на stdout как JSON.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class TriggerGetCommandTests
{
    /// <summary>
    /// Получение триггера по идентификатору должно сделать
    /// <c>GET /queues/{queue}/triggers/{id}</c> и распечатать тело ответа.
    /// </summary>
    [Test]
    public async Task Get_ById_ReturnsJson()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        string? path = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            path = req.RequestUri!.AbsolutePath;
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":17,"name":"t"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "automation", "trigger", "get", "17", "--queue", "DEV" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(path!.EndsWith("/queues/DEV/triggers/17", StringComparison.Ordinal)).IsTrue();
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("id").GetInt32()).IsEqualTo(17);
    }
}
