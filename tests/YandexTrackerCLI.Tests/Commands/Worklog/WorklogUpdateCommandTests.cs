using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Worklog;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt worklog update &lt;issue-key&gt; &lt;worklog-id&gt;</c>:
/// два режима payload (typed через <c>--comment</c>/<c>--duration</c>/<c>--start</c> и raw
/// через <c>--json-file</c>). Путь запроса — <c>PATCH /v3/issues/{key}/worklog/{id}</c>.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class WorklogUpdateCommandTests
{
    /// <summary>
    /// Typed-режим: указан только <c>--comment</c> — в теле объект <c>{"comment":"..."}</c>,
    /// уходит PATCH на <c>/issues/DEV-1/worklog/42</c>.
    /// </summary>
    [Test]
    public async Task WorklogUpdate_TypedCommentOnly_Patches()
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
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":42,"comment":"new"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "worklog", "update", "DEV-1", "42", "--comment", "new" },
            sw,
            er);
        await Assert.That(exit).IsEqualTo(0);

        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Patch);
        await Assert.That(capturedPath!.EndsWith("/issues/DEV-1/worklog/42", StringComparison.Ordinal)).IsTrue();
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("comment").GetString()).IsEqualTo("new");
        await Assert.That(doc.RootElement.TryGetProperty("duration", out _)).IsFalse();
    }

    /// <summary>
    /// Raw-режим: содержимое <c>--json-file</c> уходит в тело запроса без трансформаций.
    /// </summary>
    [Test]
    public async Task WorklogUpdate_JsonFile_SendsRawBody()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var path = Path.Combine(Path.GetTempPath(), "wl-upd-" + Guid.NewGuid().ToString("N") + ".json");
        var raw = """{"duration":"PT3H"}""";
        await File.WriteAllTextAsync(path, raw);

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":42}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "worklog", "update", "DEV-1", "42", "--json-file", path },
            sw,
            er);
        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("duration").GetString()).IsEqualTo("PT3H");
    }

    /// <summary>
    /// Merge: <c>--json-file</c> + inline <c>--duration</c> => override побеждает.
    /// </summary>
    [Test]
    public async Task WorklogUpdate_JsonFile_WithInlineOverride_Merges()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var path = Path.Combine(Path.GetTempPath(), "wl-upd-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, """{"duration":"PT1H","comment":"old"}""");

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":42}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "worklog", "update", "DEV-1", "42", "--json-file", path, "--duration", "PT2H" },
            sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("duration").GetString()).IsEqualTo("PT2H");
        await Assert.That(doc.RootElement.GetProperty("comment").GetString()).IsEqualTo("old");
    }
}
