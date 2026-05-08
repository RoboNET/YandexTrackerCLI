using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Issue;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt issue update &lt;key&gt;</c>: два режима задания payload
/// (typed args и raw JSON через <c>--json-file</c>/<c>--json-stdin</c>), проверка
/// взаимного исключения и отказ при отсутствии изменений. Мутируют глобальное state
/// (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class IssueUpdateCommandTests
{
    /// <summary>
    /// Typed-режим: указано <c>--summary</c> — собирается компактный JSON-объект и уходит
    /// <c>PATCH /v3/issues/DEV-1</c>. Exit = 0.
    /// </summary>
    [Test]
    public async Task Update_TypedArgs_PatchesBody()
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
            r.Content = new StringContent("""{"key":"DEV-1","summary":"upd"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "update", "DEV-1", "--summary", "upd" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Patch);
        await Assert.That(capturedPath!.EndsWith("/issues/DEV-1", StringComparison.Ordinal)).IsTrue();
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("summary").GetString()).IsEqualTo("upd");
    }

    /// <summary>
    /// Raw-режим: содержимое <c>--json-file</c> уходит в тело запроса без трансформаций.
    /// </summary>
    [Test]
    public async Task Update_JsonFile_SendsRawBody()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var path = Path.Combine(Path.GetTempPath(), "u-" + Guid.NewGuid().ToString("N") + ".json");
        var raw = """{"customFields":{"story_points":5}}""";
        await File.WriteAllTextAsync(path, raw);

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"key":"DEV-1"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "update", "DEV-1", "--json-file", path }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("customFields").GetProperty("story_points").GetInt32()).IsEqualTo(5);
    }

    /// <summary>
    /// Merge: <c>--json-file</c> + scalar inline <c>--summary</c> => override побеждает.
    /// </summary>
    [Test]
    public async Task Update_JsonFile_WithInlineOverride_Merges()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var path = Path.Combine(Path.GetTempPath(), "u-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, """{"summary":"old","customFields":{"story_points":5}}""");

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"key":"DEV-1"}""", Encoding.UTF8, "application/json"),
            };
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "issue", "update", "DEV-1", "--json-file", path, "--summary", "new" },
            sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("summary").GetString()).IsEqualTo("new");
        await Assert.That(doc.RootElement.GetProperty("customFields").GetProperty("story_points").GetInt32()).IsEqualTo(5);
    }

    /// <summary>
    /// Без typed-флагов и без <c>--json-file</c>/<c>--json-stdin</c> — нечего обновлять,
    /// exit 2.
    /// </summary>
    [Test]
    public async Task Update_NoChanges_Returns_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "update", "DEV-1" }, sw, er);
        await Assert.That(exit).IsEqualTo(2);
    }

}
