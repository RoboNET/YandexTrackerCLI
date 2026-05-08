namespace YandexTrackerCLI.Tests.Commands.Automation.Trigger;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Tests.Http;

/// <summary>
/// End-to-end тесты команд <c>yt automation trigger activate/deactivate</c>:
/// проверяют отправку PATCH-запроса с фиксированным телом
/// <c>{"active":true}</c> или <c>{"active":false}</c>.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class TriggerActivateDeactivateCommandTests
{
    /// <summary>
    /// <c>activate</c> отправляет PATCH с телом <c>{"active":true}</c>.
    /// </summary>
    [Test]
    public async Task Activate_PatchesActiveTrue()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        string? body = null;
        HttpMethod? method = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            method = req.Method;
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":17,"active":true}""",
                Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var exit = await env.Invoke(
            new[] { "automation", "trigger", "activate", "17", "--queue", "DEV" },
            new StringWriter(), new StringWriter());

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(method).IsEqualTo(HttpMethod.Patch);
        using var doc = JsonDocument.Parse(body!);
        await Assert.That(doc.RootElement.GetProperty("active").GetBoolean()).IsTrue();
    }

    /// <summary>
    /// <c>deactivate</c> отправляет PATCH с телом <c>{"active":false}</c>.
    /// </summary>
    [Test]
    public async Task Deactivate_PatchesActiveFalse()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        string? body = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":17,"active":false}""",
                Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var exit = await env.Invoke(
            new[] { "automation", "trigger", "deactivate", "17", "--queue", "DEV" },
            new StringWriter(), new StringWriter());

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(body!);
        await Assert.That(doc.RootElement.GetProperty("active").GetBoolean()).IsFalse();
    }
}
