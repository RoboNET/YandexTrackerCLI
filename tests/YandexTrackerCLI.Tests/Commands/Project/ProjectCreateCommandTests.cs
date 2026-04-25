using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Project;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt project create</c>: только raw-режим. Без
/// <c>--json-file</c>/<c>--json-stdin</c> — exit 2 и HTTP не вызывается. Мутируют
/// глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ProjectCreateCommandTests
{
    /// <summary>
    /// Raw-режим: содержимое <c>--json-file</c> уходит в тело POST без трансформаций,
    /// путь заканчивается на <c>/entities/project</c>.
    /// </summary>
    [Test]
    public async Task Create_JsonFile_PostsRawBody()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var path = Path.Combine(Path.GetTempPath(), "project-create-" + Guid.NewGuid().ToString("N") + ".json");
        var raw = """{"name":"Alpha","queues":["DEV"]}""";
        await File.WriteAllTextAsync(path, raw);

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
                Content = new StringContent("""{"id":42}""", Encoding.UTF8, "application/json"),
            };
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "project", "create", "--json-file", path },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Post);
        await Assert.That(capturedPath!.EndsWith("/entities/project", StringComparison.Ordinal)).IsTrue();
        await Assert.That(capturedBody).IsEqualTo(raw);
    }

    /// <summary>
    /// Без <c>--json-file</c>/<c>--json-stdin</c>: exit 2, stderr содержит
    /// <c>error.code == "invalid_args"</c>, HTTP не вызывается.
    /// </summary>
    [Test]
    public async Task Create_NoPayload_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "project", "create" }, sw, er);

        await Assert.That(exit).IsEqualTo(2);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }
}
