namespace YandexTrackerCLI.Tests.Commands.Automation.Autoaction;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Tests.Http;

/// <summary>
/// End-to-end тесты группы команд <c>yt automation autoaction</c>:
/// list/get/create/update/delete/activate/deactivate.
/// Зеркалит набор тестов триггеров — путь ресурса <c>/queues/{q}/autoactions/</c>.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class AutoactionCommandsTests
{
    /// <summary>
    /// Список автодействий очереди возвращается одной страницей и печатается как JSON-массив,
    /// при этом запрос идёт по адресу <c>/queues/{queue}/autoactions/</c>.
    /// </summary>
    [Test]
    public async Task List_PagedResponse_AggregatesItems()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        string? capturedPath = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedPath = req.RequestUri!.AbsolutePath + "?" + req.RequestUri.Query.TrimStart('?');
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"id":1,"name":"a1"},{"id":2,"name":"a2"}]""",
                    Encoding.UTF8,
                    "application/json"),
            };
            r.Headers.Add("X-Total-Pages", "1");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "automation", "autoaction", "list", "--queue", "DEV" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedPath!.Contains("/queues/DEV/autoactions/")).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetArrayLength()).IsEqualTo(2);
        await Assert.That(doc.RootElement[0].GetProperty("name").GetString()).IsEqualTo("a1");
    }

    /// <summary>
    /// Получение автодействия по идентификатору должно сделать
    /// <c>GET /queues/{queue}/autoactions/{id}</c> и распечатать тело ответа.
    /// </summary>
    [Test]
    public async Task Get_ById_ReturnsJson()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        string? path = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            path = req.RequestUri!.AbsolutePath;
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":17,"name":"a"}""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "automation", "autoaction", "get", "17", "--queue", "DEV" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(path!.EndsWith("/queues/DEV/autoactions/17", StringComparison.Ordinal)).IsTrue();
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("id").GetInt32()).IsEqualTo(17);
    }

    /// <summary>
    /// Файл задаёт базовое тело, а inline <c>--name</c> и <c>--active</c>
    /// мерджатся поверх перед POST.
    /// </summary>
    [Test]
    public async Task Create_FileWithInlineNameOverride_MergesAndPosts()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var path = Path.Combine(Path.GetTempPath(), "aa-" + Guid.NewGuid().ToString("N") + ".json");
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
            new[] { "automation", "autoaction", "create",
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
            new[] { "automation", "autoaction", "update", "17",
                    "--queue", "DEV", "--name", "renamed" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Patch);
        await Assert.That(capturedPath!.EndsWith("/queues/DEV/autoactions/17", StringComparison.Ordinal)).IsTrue();
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("renamed");
    }

    /// <summary>
    /// 204-ответ → stdout содержит JSON <c>{"deleted":"&lt;id&gt;"}</c>.
    /// </summary>
    [Test]
    public async Task Delete_204_PrintsDeletedMarker()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler().Push(req =>
            new HttpResponseMessage(HttpStatusCode.NoContent));
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "automation", "autoaction", "delete", "17", "--queue", "DEV" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("deleted").GetString()).IsEqualTo("17");
    }

    /// <summary>
    /// <c>activate</c> отправляет PATCH с телом <c>{"active":true}</c>.
    /// </summary>
    [Test]
    public async Task Activate_PatchesActiveTrue()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        string? body = null;
        HttpMethod? method = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            method = req.Method;
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":17,"active":true}""",
                Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var exit = await env.Invoke(
            new[] { "automation", "autoaction", "activate", "17", "--queue", "DEV" },
            new StringWriter(), new StringWriter());

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(method).IsEqualTo(HttpMethod.Patch);
        using var doc = JsonDocument.Parse(body!);
        await Assert.That(doc.RootElement.GetProperty("active").GetBoolean()).IsTrue();
    }

    /// <summary>
    /// <c>deactivate</c> отправляет PATCH с телом <c>{"active":false}</c>.
    /// </summary>
    [Test]
    public async Task Deactivate_PatchesActiveFalse()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        string? body = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":17,"active":false}""",
                Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var exit = await env.Invoke(
            new[] { "automation", "autoaction", "deactivate", "17", "--queue", "DEV" },
            new StringWriter(), new StringWriter());

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(body!);
        await Assert.That(doc.RootElement.GetProperty("active").GetBoolean()).IsFalse();
    }
}
