namespace YandexTrackerCLI.Tests.Commands.Automation.Trigger;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Tests.Http;

/// <summary>
/// End-to-end тесты команды <c>yt automation trigger create</c>:
/// проверяют merge JSON-тела (файл + inline override'ы), сборку
/// тела из inline-флагов, а также отказ при отсутствии источника.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class TriggerCreateCommandTests
{
    /// <summary>
    /// Файл задаёт базовое тело, а inline <c>--name</c> и <c>--active</c>
    /// мерджатся поверх перед POST.
    /// </summary>
    [Test]
    public async Task Create_FileWithInlineNameOverride_MergesAndPosts()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var path = Path.Combine(Path.GetTempPath(), "trg-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path,
            """{"name":"old","actions":[{"type":"changeStatus","value":"closed"}]}""");

        string? capturedBody = null;
        HttpMethod? capturedMethod = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedMethod = req.Method;
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.Created);
            r.Content = new StringContent("""{"id":42}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "automation", "trigger", "create",
                    "--queue", "DEV", "--json-file", path, "--name", "new", "--active" },
            sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Post);

        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("new");
        await Assert.That(doc.RootElement.GetProperty("active").GetBoolean()).IsTrue();
        await Assert.That(doc.RootElement.GetProperty("actions").GetArrayLength()).IsEqualTo(1);

        File.Delete(path);
    }

    /// <summary>
    /// Без файла и stdin тело собирается только из inline-флагов.
    /// </summary>
    [Test]
    public async Task Create_InlineOnly_BuildsBodyFromFlags()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.Created);
            r.Content = new StringContent("""{"id":1}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "automation", "trigger", "create",
                    "--queue", "DEV", "--name", "X", "--inactive" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("X");
        await Assert.That(doc.RootElement.GetProperty("active").GetBoolean()).IsFalse();
    }

    /// <summary>
    /// Без источника и без override'ов команда падает с InvalidArgs (exit 2).
    /// </summary>
    [Test]
    public async Task Create_NoSource_NoOverrides_FailsInvalidArgs()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "automation", "trigger", "create", "--queue", "DEV" }, sw, er);

        await Assert.That(exit).IsEqualTo(2);
    }
}
