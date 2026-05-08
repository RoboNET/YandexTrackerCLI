using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Worklog;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt worklog add &lt;issue-key&gt;</c>: два режима payload
/// (<c>--duration</c>/<c>--comment</c>/<c>--start</c> и <c>--json-file</c>), валидация
/// ISO 8601 duration и ошибка при полном отсутствии полезной нагрузки.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class WorklogAddCommandTests
{
    /// <summary>
    /// Typed-режим: указаны <c>--duration PT1H --comment "hi"</c> — собирается
    /// <c>{"duration":"PT1H","comment":"hi"}</c> и уходит <c>POST /v3/issues/DEV-1/worklog</c>.
    /// </summary>
    [Test]
    public async Task WorklogAdd_TypedDurationAndComment_BuildsJsonBody_AndPosts()
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
            r.Content = new StringContent("""{"id":7}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "worklog", "add", "DEV-1", "--duration", "PT1H", "--comment", "hi" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Post);
        await Assert.That(capturedPath!.EndsWith("/issues/DEV-1/worklog", StringComparison.Ordinal)).IsTrue();

        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("duration").GetString()).IsEqualTo("PT1H");
        await Assert.That(doc.RootElement.GetProperty("comment").GetString()).IsEqualTo("hi");
    }

    /// <summary>
    /// Raw-режим: содержимое <c>--json-file</c> уходит в тело запроса без трансформаций.
    /// </summary>
    [Test]
    public async Task WorklogAdd_JsonFile_SendsContent_Verbatim()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var path = Path.Combine(Path.GetTempPath(), "wl-add-" + Guid.NewGuid().ToString("N") + ".json");
        var raw = """{"duration":"PT2H","start":"2024-01-15T10:00:00+03:00"}""";
        await File.WriteAllTextAsync(path, raw);

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.Created);
            r.Content = new StringContent("""{"id":8}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "worklog", "add", "DEV-1", "--json-file", path }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("duration").GetString()).IsEqualTo("PT2H");
        await Assert.That(doc.RootElement.GetProperty("start").GetString()).IsEqualTo("2024-01-15T10:00:00+03:00");
    }

    /// <summary>
    /// Merge: <c>--json-file</c> + inline <c>--comment</c>/<c>--duration</c> => override побеждает.
    /// </summary>
    [Test]
    public async Task WorklogAdd_JsonFile_WithInlineOverride_Merges()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var path = Path.Combine(Path.GetTempPath(), "wl-add-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, """{"duration":"PT1H","start":"2024-01-15T10:00:00+03:00"}""");

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.Created);
            r.Content = new StringContent("""{"id":9}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "worklog", "add", "DEV-1", "--json-file", path, "--duration", "PT3H", "--comment", "merged" },
            sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("duration").GetString()).IsEqualTo("PT3H");
        await Assert.That(doc.RootElement.GetProperty("start").GetString()).IsEqualTo("2024-01-15T10:00:00+03:00");
        await Assert.That(doc.RootElement.GetProperty("comment").GetString()).IsEqualTo("merged");
    }

    /// <summary>
    /// Невалидный ISO 8601 duration — exit 2, stderr содержит <c>error.code == "invalid_args"</c>.
    /// </summary>
    [Test]
    public async Task WorklogAdd_InvalidDuration_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "worklog", "add", "DEV-1", "--duration", "not-iso" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(2);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
    }

    /// <summary>
    /// Без typed-опций и без raw — exit 2.
    /// </summary>
    [Test]
    public async Task WorklogAdd_NoPayload_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "worklog", "add", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(2);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
    }
}
