using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Checklist;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt checklist add-item &lt;issue-key&gt;</c>: два режима
/// payload (typed через <c>--text</c>/<c>--assignee</c>/<c>--deadline</c> и raw через
/// <c>--json-file</c>) и ошибка при отсутствии <c>--text</c> в typed-режиме.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ChecklistAddItemCommandTests
{
    /// <summary>
    /// Typed-режим: <c>--text</c> + <c>--assignee</c> + <c>--deadline</c> собираются в
    /// JSON-объект и уходят POST'ом на <c>/v3/issues/DEV-1/checklistItems</c>.
    /// </summary>
    [Test]
    public async Task ChecklistAddItem_Typed_BuildsBody_AndPosts()
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
            var r = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":"i1"}""", Encoding.UTF8, "application/json"),
            };
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[]
            {
                "checklist", "add-item", "DEV-1",
                "--text", "buy milk",
                "--assignee", "alice",
                "--deadline", "2024-01-15T10:00:00+03:00",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Post);
        await Assert.That(capturedPath!.EndsWith("/issues/DEV-1/checklistItems", StringComparison.Ordinal)).IsTrue();

        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("text").GetString()).IsEqualTo("buy milk");
        await Assert.That(doc.RootElement.GetProperty("assignee").GetString()).IsEqualTo("alice");
        await Assert.That(doc.RootElement.GetProperty("deadline").GetString()).IsEqualTo("2024-01-15T10:00:00+03:00");
    }

    /// <summary>
    /// Raw-режим: содержимое <c>--json-file</c> уходит в тело без трансформаций.
    /// </summary>
    [Test]
    public async Task ChecklistAddItem_JsonFile_SendsRawBody()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var path = Path.Combine(Path.GetTempPath(), "cl-add-" + Guid.NewGuid().ToString("N") + ".json");
        var raw = """{"text":"raw","assignee":"bob"}""";
        await File.WriteAllTextAsync(path, raw);

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":"i2"}""", Encoding.UTF8, "application/json"),
            };
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "checklist", "add-item", "DEV-1", "--json-file", path },
            sw,
            er);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedBody).IsEqualTo(raw);
    }

    /// <summary>
    /// Без typed-опций и без raw — exit 2, stderr содержит <c>error.code == "invalid_args"</c>.
    /// </summary>
    [Test]
    public async Task ChecklistAddItem_NoText_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "checklist", "add-item", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(2);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
    }
}
