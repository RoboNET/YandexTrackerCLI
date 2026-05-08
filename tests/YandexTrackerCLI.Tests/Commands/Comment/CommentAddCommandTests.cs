using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Comment;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt comment add &lt;issue-key&gt;</c>: два режима payload
/// (<c>--text</c> и <c>--json-file</c>) и ошибка при отсутствии и того, и другого.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class CommentAddCommandTests
{
    /// <summary>
    /// Typed-режим: указан <c>--text</c> — собирается <c>{"text":"..."}</c> и уходит
    /// <c>POST /v3/issues/DEV-1/comments</c>. Exit = 0.
    /// </summary>
    [Test]
    public async Task CommentAdd_TypedText_BuildsJsonBody_AndPosts()
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
            var r = new HttpResponseMessage(HttpStatusCode.Created);
            r.Content = new StringContent("""{"id":"42"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "add", "DEV-1", "--text", "hello" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Post);
        await Assert.That(capturedPath!.EndsWith("/issues/DEV-1/comments", StringComparison.Ordinal)).IsTrue();

        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("text").GetString()).IsEqualTo("hello");
    }

    /// <summary>
    /// Raw-режим: содержимое <c>--json-file</c> уходит в тело запроса (после merge без override'ов).
    /// </summary>
    [Test]
    public async Task CommentAdd_JsonFile_SendsContent()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var path = Path.Combine(Path.GetTempPath(), "comm-add-" + Guid.NewGuid().ToString("N") + ".json");
        var raw = """{"text":"from file","attachments":["A"]}""";
        await File.WriteAllTextAsync(path, raw);

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.Created);
            r.Content = new StringContent("""{"id":"43"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "add", "DEV-1", "--json-file", path }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("text").GetString()).IsEqualTo("from file");
        await Assert.That(doc.RootElement.GetProperty("attachments").GetArrayLength()).IsEqualTo(1);
    }

    /// <summary>
    /// Merge: <c>--json-file</c> с base text + inline <c>--text</c> => inline override побеждает.
    /// </summary>
    [Test]
    public async Task CommentAdd_JsonFile_WithInlineOverride_Merges()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var path = Path.Combine(Path.GetTempPath(), "comm-add-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, """{"text":"old","attachments":["A"]}""");

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.Created);
            r.Content = new StringContent("""{"id":"44"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "add", "DEV-1", "--json-file", path, "--text", "new" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("text").GetString()).IsEqualTo("new");
        await Assert.That(doc.RootElement.GetProperty("attachments").GetArrayLength()).IsEqualTo(1);
    }

    /// <summary>
    /// Без <c>--text</c> и без <c>--json-file</c>/<c>--json-stdin</c> — exit 2.
    /// </summary>
    [Test]
    public async Task CommentAdd_NoPayload_Returns_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "comment", "add", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(2);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString()).IsEqualTo("invalid_args");
    }
}
