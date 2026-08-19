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

    /// <summary>
    /// <c>activate --version N</c> и <c>deactivate --version N</c> добавляют
    /// <c>?version=N</c> в PATCH-URL; без флага query остаётся пустым.
    /// </summary>
    [Test]
    public async Task ActivateDeactivate_VersionFlagControlsQuery()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var queries = new List<string>();
        HttpResponseMessage Handle(HttpRequestMessage req)
        {
            queries.Add(req.RequestUri!.Query);
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":17}""", Encoding.UTF8, "application/json");
            return r;
        }

        env.InnerHandler = new TestHttpMessageHandler().Push(Handle).Push(Handle).Push(Handle);

        var activate = await env.Invoke(
            new[] { "automation", "trigger", "activate", "17", "--queue", "DEV", "--version", "3" },
            new StringWriter(), new StringWriter());
        var deactivate = await env.Invoke(
            new[] { "automation", "trigger", "deactivate", "17", "--queue", "DEV", "--version", "4" },
            new StringWriter(), new StringWriter());
        var noVersion = await env.Invoke(
            new[] { "automation", "trigger", "activate", "17", "--queue", "DEV" },
            new StringWriter(), new StringWriter());

        await Assert.That(activate).IsEqualTo(0);
        await Assert.That(deactivate).IsEqualTo(0);
        await Assert.That(noVersion).IsEqualTo(0);
        await Assert.That(queries[0]).IsEqualTo("?version=3");
        await Assert.That(queries[1]).IsEqualTo("?version=4");
        await Assert.That(queries[2].Contains("version", StringComparison.Ordinal)).IsFalse();
    }
}
