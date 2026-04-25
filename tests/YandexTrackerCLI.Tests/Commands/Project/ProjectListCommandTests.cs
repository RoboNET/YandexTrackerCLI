using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Project;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt project list</c>: <c>POST /v3/entities/project/_search</c>
/// с опциональным телом-фильтром (<c>--json-file</c>/<c>--json-stdin</c>). По умолчанию тело —
/// пустой объект <c>{}</c>. Мутируют глобальное state (env + Console + AsyncLocal), поэтому
/// последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ProjectListCommandTests
{
    /// <summary>
    /// Без <c>--json-file</c>/<c>--json-stdin</c>: метод POST, path заканчивается на
    /// <c>/entities/project/_search</c>, тело — пустой объект <c>{}</c>.
    /// </summary>
    [Test]
    public async Task List_Default_PostsEmptyFilter()
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
            r.Content = new StringContent("""[{"id":1},{"id":2}]""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "project", "list" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Post);
        await Assert.That(capturedPath!.EndsWith("/entities/project/_search", StringComparison.Ordinal)).IsTrue();
        await Assert.That(capturedBody).IsEqualTo("{}");

        using var doc = JsonDocument.Parse(sw.ToString());
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToArray();
        await Assert.That(ids).IsEquivalentTo(new[] { 1, 2 });
    }

    /// <summary>
    /// C <c>--json-file</c>: в тело POST-запроса уходит ровно содержимое файла (без
    /// перепаковки).
    /// </summary>
    [Test]
    public async Task List_WithJsonFile_SendsCustomFilter()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var path = Path.Combine(Path.GetTempPath(), "project-list-" + Guid.NewGuid().ToString("N") + ".json");
        var raw = """{"queue":"DEV"}""";
        await File.WriteAllTextAsync(path, raw);

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""[]""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "project", "list", "--json-file", path },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedBody).IsEqualTo(raw);
    }
}
