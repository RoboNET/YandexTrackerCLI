using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Issue;

using System.Net;
using System.Net.Http;
using System.Text;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt issue batch</c>: <c>POST /v3/bulkchange</c> с raw
/// JSON-телом из <c>--json-file</c>, валидация обязательности payload.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class IssueBatchCommandTests
{
    /// <summary>
    /// <c>--json-file</c> — команда шлёт <c>POST /v3/bulkchange</c> с телом, равным
    /// содержимому файла. Exit = 0.
    /// </summary>
    [Test]
    public async Task Batch_JsonFile_PostsToBulkchange()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var filePath = Path.Combine(Path.GetTempPath(), "batch-" + Guid.NewGuid() + ".json");
        var raw = """{"operations":[{"key":"DEV-1","patch":{"priority":"high"}}]}""";
        await File.WriteAllTextAsync(filePath, raw);

        HttpMethod? method = null;
        string? path = null;
        string? body = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            method = req.Method;
            path = req.RequestUri!.AbsolutePath;
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"ok"}""", Encoding.UTF8, "application/json"),
            };
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "issue", "batch", "--json-file", filePath },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(method).IsEqualTo(HttpMethod.Post);
        await Assert.That(path!.EndsWith("/bulkchange", StringComparison.Ordinal)).IsTrue();
        await Assert.That(body).IsEqualTo(raw);
    }

    /// <summary>
    /// Без <c>--json-file</c> и без <c>--json-stdin</c> — exit 2
    /// (<see cref="YandexTrackerCLI.Core.Api.Errors.ErrorCode.InvalidArgs"/>).
    /// </summary>
    [Test]
    public async Task Batch_NoPayload_Returns_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "batch" }, sw, er);

        await Assert.That(exit).IsEqualTo(2);
    }
}
