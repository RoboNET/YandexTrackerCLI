namespace YandexTrackerCLI.Tests.Commands.Automation.Trigger;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Tests.Http;

/// <summary>
/// End-to-end тесты команды <c>yt automation trigger update</c>:
/// проверяют PATCH-запрос с merge-телом из inline-флагов.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class TriggerUpdateCommandTests
{
    /// <summary>
    /// Inline <c>--name</c> приводит к PATCH с телом, содержащим только это поле.
    /// </summary>
    [Test]
    public async Task Update_InlineNameOnly_PatchesWithMergedBody()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        string? capturedPath = null;
        string? capturedBody = null;
        HttpMethod? capturedMethod = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedMethod = req.Method;
            capturedPath = req.RequestUri!.AbsolutePath;
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":17}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "automation", "trigger", "update", "17",
                    "--queue", "DEV", "--name", "renamed" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Patch);
        await Assert.That(capturedPath!.EndsWith("/queues/DEV/triggers/17", StringComparison.Ordinal)).IsTrue();
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("renamed");
    }

    /// <summary>
    /// Действие <c>Update</c> в read-формате конвертируется в write-формат
    /// перед отправкой PATCH.
    /// </summary>
    [Test]
    public async Task Update_ReadFormatUpdateAction_PatchesWriteFormatBody()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var file = Path.Combine(env.Root, "trg-read-format.json");
        await File.WriteAllTextAsync(file,
            """{"actions":[{"type":"Update","id":3,"update":[{"field":{"id":"frontier","display":"Рубеж"},"update":{"set":90}}]}]}""");

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":17}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var exit = await env.Invoke(
            new[] { "automation", "trigger", "update", "17", "--queue", "DEV", "--json-file", file },
            new StringWriter(), new StringWriter());

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        var update = doc.RootElement.GetProperty("actions")[0].GetProperty("update");
        await Assert.That(update.ValueKind).IsEqualTo(JsonValueKind.Object);
        await Assert.That(update.GetProperty("frontier").GetInt32()).IsEqualTo(90);
    }

    /// <summary>
    /// <c>--version</c> уходит query-параметром <c>?version=N</c> в PATCH-URL,
    /// без флага query остаётся пустым.
    /// </summary>
    [Test]
    public async Task Update_VersionFlagControlsQuery()
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

        env.InnerHandler = new TestHttpMessageHandler().Push(Handle).Push(Handle);

        var withVersion = await env.Invoke(
            new[] { "automation", "trigger", "update", "17",
                    "--queue", "DEV", "--name", "renamed", "--version", "5" },
            new StringWriter(), new StringWriter());
        var withoutVersion = await env.Invoke(
            new[] { "automation", "trigger", "update", "17",
                    "--queue", "DEV", "--name", "renamed" },
            new StringWriter(), new StringWriter());

        await Assert.That(withVersion).IsEqualTo(0);
        await Assert.That(withoutVersion).IsEqualTo(0);
        await Assert.That(queries[0]).IsEqualTo("?version=5");
        await Assert.That(queries[1].Contains("version", StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>
    /// Поле <c>version</c> из тела уходит в query и вырезается из отправляемого JSON.
    /// </summary>
    [Test]
    public async Task Update_VersionInBody_MovesToQueryAndIsStripped()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var file = Path.Combine(env.Root, "trg-with-version.json");
        await File.WriteAllTextAsync(file, """{"id":17,"version":8,"name":"a"}""");

        string? query = null;
        string? capturedBody = null;
        env.InnerHandler = new TestHttpMessageHandler().Push(req =>
        {
            query = req.RequestUri!.Query;
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":17}""", Encoding.UTF8, "application/json");
            return r;
        });

        var exit = await env.Invoke(
            new[] { "automation", "trigger", "update", "17", "--queue", "DEV", "--json-file", file },
            new StringWriter(), new StringWriter());

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(query).IsEqualTo("?version=8");
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.TryGetProperty("version", out _)).IsFalse();
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("a");
    }
}
