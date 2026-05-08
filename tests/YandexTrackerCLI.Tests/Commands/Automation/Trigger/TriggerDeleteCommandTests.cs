namespace YandexTrackerCLI.Tests.Commands.Automation.Trigger;

using System.Net;
using System.Net.Http;
using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Tests.Http;

/// <summary>
/// End-to-end тесты команды <c>yt automation trigger delete</c>:
/// проверяют печать success-маркера при ответе 204 No Content.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class TriggerDeleteCommandTests
{
    /// <summary>
    /// 204-ответ → stdout содержит JSON <c>{"deleted":"&lt;id&gt;"}</c>.
    /// </summary>
    [Test]
    public async Task Delete_204_PrintsDeletedMarker()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler().Push(req =>
            new HttpResponseMessage(HttpStatusCode.NoContent));
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "automation", "trigger", "delete", "17", "--queue", "DEV" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("deleted").GetString()).IsEqualTo("17");
    }
}
