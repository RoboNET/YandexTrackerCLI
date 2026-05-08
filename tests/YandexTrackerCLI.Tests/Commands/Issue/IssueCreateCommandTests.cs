using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Issue;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt issue create</c>: два режима задания payload
/// (typed args и raw JSON через <c>--json-file</c>/<c>--json-stdin</c>),
/// валидация обязательных полей и взаимного исключения режимов.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class IssueCreateCommandTests
{
    /// <summary>
    /// Typed-режим: все поля явно — собирается компактный JSON-объект и отправляется
    /// <c>POST /v3/issues</c>. Exit = 0.
    /// </summary>
    [Test]
    public async Task Create_TypedArgs_BuildsJsonBody_AndPosts()
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
            r.Content = new StringContent("""{"key":"DEV-1"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[]
            {
                "issue", "create",
                "--queue", "DEV",
                "--summary", "hello world",
                "--description", "desc",
                "--type", "bug",
                "--priority", "minor",
                "--assignee", "user1",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Post);
        await Assert.That(capturedPath!.EndsWith("/issues", StringComparison.Ordinal)).IsTrue();

        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("queue").GetString()).IsEqualTo("DEV");
        await Assert.That(doc.RootElement.GetProperty("summary").GetString()).IsEqualTo("hello world");
        await Assert.That(doc.RootElement.GetProperty("description").GetString()).IsEqualTo("desc");
        await Assert.That(doc.RootElement.GetProperty("type").GetString()).IsEqualTo("bug");
        await Assert.That(doc.RootElement.GetProperty("priority").GetString()).IsEqualTo("minor");
        await Assert.That(doc.RootElement.GetProperty("assignee").GetString()).IsEqualTo("user1");
    }

    /// <summary>
    /// Raw-режим: содержимое <c>--json-file</c> уходит в тело запроса без трансформаций.
    /// </summary>
    [Test]
    public async Task Create_JsonFile_SendsContent_Verbatim()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var path = Path.Combine(Path.GetTempPath(), "create-" + Guid.NewGuid().ToString("N") + ".json");
        var raw = """{"queue":"DEV","summary":"from file","customFields":{"x":"y"}}""";
        await File.WriteAllTextAsync(path, raw);

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.Created);
            r.Content = new StringContent("""{"key":"DEV-2"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "create", "--json-file", path }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("queue").GetString()).IsEqualTo("DEV");
        await Assert.That(doc.RootElement.GetProperty("summary").GetString()).IsEqualTo("from file");
        await Assert.That(doc.RootElement.GetProperty("customFields").GetProperty("x").GetString()).IsEqualTo("y");
    }

    /// <summary>
    /// Merge: <c>--json-file</c> + scalar inline-флаги => override побеждает,
    /// неоверайдные поля сохраняются.
    /// </summary>
    [Test]
    public async Task Create_JsonFile_WithInlineOverride_Merges()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var path = Path.Combine(Path.GetTempPath(), "create-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path,
            """{"queue":"DEV","summary":"old","customFields":{"x":"y"}}""");

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.Created);
            r.Content = new StringContent("""{"key":"DEV-3"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "issue", "create", "--json-file", path, "--summary", "new", "--priority", "minor" },
            sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("queue").GetString()).IsEqualTo("DEV");
        await Assert.That(doc.RootElement.GetProperty("summary").GetString()).IsEqualTo("new");
        await Assert.That(doc.RootElement.GetProperty("priority").GetString()).IsEqualTo("minor");
        await Assert.That(doc.RootElement.GetProperty("customFields").GetProperty("x").GetString()).IsEqualTo("y");
    }

    /// <summary>
    /// Без единого аргумента — exit 2 (нет ни typed-полей, ни payload-флагов).
    /// </summary>
    [Test]
    public async Task Create_NoArgs_NoPayload_ReturnsInvalidArgs()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "create" }, sw, er);
        await Assert.That(exit).IsEqualTo(2);
    }

    /// <summary>
    /// Typed-режим без <c>--summary</c> — exit 2 (оба поля обязательны).
    /// </summary>
    [Test]
    public async Task Create_TypedArgs_WithoutSummary_Returns_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "create", "--queue", "DEV" }, sw, er);
        await Assert.That(exit).IsEqualTo(2);
    }
}
