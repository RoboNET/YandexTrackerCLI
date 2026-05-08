using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Comment;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt comment update &lt;issue-key&gt; &lt;comment-id&gt;</c>:
/// два режима payload (<c>--text</c> и <c>--json-file</c>). Путь запроса —
/// <c>PATCH /v3/issues/{key}/comments/{id}</c>. Мутируют глобальное state
/// (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class CommentUpdateCommandTests
{
    /// <summary>
    /// Typed-режим: <c>--text</c> — собирается <c>{"text":"..."}</c> и уходит
    /// <c>PATCH /v3/issues/DEV-1/comments/42</c>. Exit = 0.
    /// </summary>
    [Test]
    public async Task CommentUpdate_TypedText_Patches()
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
            r.Content = new StringContent("""{"id":"42","text":"upd"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "update", "DEV-1", "42", "--text", "upd" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Patch);
        await Assert.That(capturedPath!.EndsWith("/issues/DEV-1/comments/42", StringComparison.Ordinal)).IsTrue();
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("text").GetString()).IsEqualTo("upd");
    }

    /// <summary>
    /// Raw-режим: содержимое <c>--json-file</c> уходит в тело запроса без трансформаций.
    /// </summary>
    [Test]
    public async Task CommentUpdate_JsonFile_SendsRawBody()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var path = Path.Combine(Path.GetTempPath(), "comm-upd-" + Guid.NewGuid().ToString("N") + ".json");
        var raw = """{"text":"raw","longText":{"ops":[]}}""";
        await File.WriteAllTextAsync(path, raw);

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":"42"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "update", "DEV-1", "42", "--json-file", path }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("text").GetString()).IsEqualTo("raw");
        await Assert.That(doc.RootElement.GetProperty("longText").ValueKind).IsEqualTo(JsonValueKind.Object);
    }

    /// <summary>
    /// Merge: <c>--json-file</c> с base text + inline <c>--text</c> => inline override побеждает.
    /// </summary>
    [Test]
    public async Task CommentUpdate_JsonFile_WithInlineOverride_Merges()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var path = Path.Combine(Path.GetTempPath(), "comm-upd-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, """{"text":"old","longText":{"ops":[]}}""");

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":"42"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "update", "DEV-1", "42", "--json-file", path, "--text", "new" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("text").GetString()).IsEqualTo("new");
        await Assert.That(doc.RootElement.GetProperty("longText").ValueKind).IsEqualTo(JsonValueKind.Object);
    }
}
