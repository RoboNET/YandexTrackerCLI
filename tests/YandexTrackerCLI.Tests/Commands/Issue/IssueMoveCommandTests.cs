using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Issue;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt issue move &lt;key&gt;</c>: typed режим <c>--to-queue</c>
/// и raw режим <c>--json-file</c>. Мутируют глобальное state (env + Console + AsyncLocal),
/// поэтому выполняются последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class IssueMoveCommandTests
{
    /// <summary>
    /// <c>--to-queue NEW</c> — уходит <c>POST /v3/issues/DEV-1/_move</c> с typed телом
    /// <c>{"queue":"NEW"}</c>. Exit = 0.
    /// </summary>
    [Test]
    public async Task Move_ToQueue_PostsJsonBody_WithCorrectPath()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        HttpMethod? method = null;
        string? path = null;
        string? body = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            method = req.Method;
            path = req.RequestUri!.AbsolutePath;
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "move", "DEV-1", "--to-queue", "NEW" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(method).IsEqualTo(HttpMethod.Post);
        await Assert.That(path!.EndsWith("/issues/DEV-1/_move", StringComparison.Ordinal)).IsTrue();
        await Assert.That(body).IsEqualTo("""{"queue":"NEW"}""");
    }

    /// <summary>
    /// <c>--json-file</c> — содержимое файла уходит в тело запроса без трансформаций.
    /// </summary>
    [Test]
    public async Task Move_JsonFile_SendsRawBody()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var filePath = Path.Combine(Path.GetTempPath(), "mv-" + Guid.NewGuid() + ".json");
        var raw = """{"queue":"NEW","notify":false}""";
        await File.WriteAllTextAsync(filePath, raw);

        string? body = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "issue", "move", "DEV-1", "--json-file", filePath },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(body!);
        await Assert.That(doc.RootElement.GetProperty("queue").GetString()).IsEqualTo("NEW");
        await Assert.That(doc.RootElement.GetProperty("notify").GetBoolean()).IsFalse();
    }

    /// <summary>
    /// Merge: <c>--json-file</c> + scalar inline <c>--to-queue</c> => override побеждает.
    /// </summary>
    [Test]
    public async Task Move_JsonFile_WithInlineOverride_Merges()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var filePath = Path.Combine(Path.GetTempPath(), "mv-" + Guid.NewGuid() + ".json");
        await File.WriteAllTextAsync(filePath, """{"queue":"OLD","notify":false}""");

        string? body = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "issue", "move", "DEV-1", "--json-file", filePath, "--to-queue", "NEW" },
            sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(body!);
        await Assert.That(doc.RootElement.GetProperty("queue").GetString()).IsEqualTo("NEW");
        await Assert.That(doc.RootElement.GetProperty("notify").GetBoolean()).IsFalse();
    }

    /// <summary>
    /// Без <c>--to-queue</c> и без <c>--json-file</c>/<c>--json-stdin</c> — exit 2
    /// (<see cref="YandexTrackerCLI.Core.Api.Errors.ErrorCode.InvalidArgs"/>).
    /// </summary>
    [Test]
    public async Task Move_NoTarget_Returns_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "move", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(2);
    }
}
