using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Checklist;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt checklist update &lt;issue-key&gt; &lt;item-id&gt;</c>:
/// два режима payload (typed через <c>--text</c>/<c>--assignee</c>/<c>--deadline</c> и raw
/// через <c>--json-file</c>). Путь запроса — <c>PATCH /v3/issues/{key}/checklistItems/{itemId}</c>.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ChecklistUpdateCommandTests
{
    /// <summary>
    /// Typed-режим: указан только <c>--text</c> — в теле объект <c>{"text":"..."}</c>,
    /// уходит PATCH на <c>/issues/DEV-1/checklistItems/i1</c>.
    /// </summary>
    [Test]
    public async Task ChecklistUpdate_TypedText_Patches()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        string? capturedBody = null;
        HttpMethod? capturedMethod = null;
        string? capturedPath = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedMethod = req.Method;
            capturedPath = req.RequestUri!.AbsolutePath;
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"i1","text":"new"}""", Encoding.UTF8, "application/json"),
            };
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "checklist", "update", "DEV-1", "i1", "--text", "new" },
            sw,
            er);
        await Assert.That(exit).IsEqualTo(0);

        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Patch);
        await Assert.That(capturedPath!.EndsWith("/issues/DEV-1/checklistItems/i1", StringComparison.Ordinal)).IsTrue();
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("text").GetString()).IsEqualTo("new");
        await Assert.That(doc.RootElement.TryGetProperty("assignee", out _)).IsFalse();
    }

    /// <summary>
    /// Raw-режим: содержимое <c>--json-file</c> уходит в тело без трансформаций.
    /// </summary>
    [Test]
    public async Task ChecklistUpdate_JsonFile_SendsRawBody()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var path = Path.Combine(Path.GetTempPath(), "cl-upd-" + Guid.NewGuid().ToString("N") + ".json");
        var raw = """{"text":"raw-text","assignee":"bob"}""";
        await File.WriteAllTextAsync(path, raw);

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"i1"}""", Encoding.UTF8, "application/json"),
            };
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "checklist", "update", "DEV-1", "i1", "--json-file", path },
            sw,
            er);
        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("text").GetString()).IsEqualTo("raw-text");
        await Assert.That(doc.RootElement.GetProperty("assignee").GetString()).IsEqualTo("bob");
    }

    /// <summary>
    /// Merge: <c>--json-file</c> + scalar inline <c>--text</c> => override побеждает.
    /// </summary>
    [Test]
    public async Task ChecklistUpdate_JsonFile_WithInlineOverride_Merges()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var path = Path.Combine(Path.GetTempPath(), "cl-upd-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, """{"text":"old","assignee":"bob"}""");

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"i1"}""", Encoding.UTF8, "application/json"),
            };
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "checklist", "update", "DEV-1", "i1", "--json-file", path, "--text", "new" },
            sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("text").GetString()).IsEqualTo("new");
        await Assert.That(doc.RootElement.GetProperty("assignee").GetString()).IsEqualTo("bob");
    }
}
