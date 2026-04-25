using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Link;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt link add &lt;issue-key&gt; --to &lt;other&gt; --type &lt;rel&gt;</c>:
/// два режима payload (typed через <c>--to</c>/<c>--type</c> и raw через <c>--json-file</c>),
/// валидация <c>--type</c> парсером (9 допустимых значений) и ошибка при частично заданных
/// typed-опциях. Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class LinkAddCommandTests
{
    /// <summary>
    /// Typed-режим: <c>--to DEV-2 --type depends-on</c> собирается в
    /// <c>{"relationship":"depends-on","issue":"DEV-2"}</c> и уходит POST'ом на
    /// <c>/v3/issues/DEV-1/links</c>.
    /// </summary>
    [Test]
    public async Task Add_Typed_PostsCorrectBody()
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
                Content = new StringContent("""{"id":42}""", Encoding.UTF8, "application/json"),
            };
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[]
            {
                "link", "add", "DEV-1",
                "--to", "DEV-2",
                "--type", "depends-on",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Post);
        await Assert.That(capturedPath!.EndsWith("/issues/DEV-1/links", StringComparison.Ordinal)).IsTrue();

        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("relationship").GetString()).IsEqualTo("depends-on");
        await Assert.That(doc.RootElement.GetProperty("issue").GetString()).IsEqualTo("DEV-2");
    }

    /// <summary>
    /// Невалидный <c>--type</c> отклоняется парсером System.CommandLine
    /// (<see cref="System.CommandLine.Option{T}.AcceptOnlyFromAmong(string[])"/>) — non-zero
    /// exit, HTTP не вызывается.
    /// </summary>
    [Test]
    public async Task Add_InvalidType_RejectedByParser()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "link", "add", "DEV-1", "--to", "DEV-2", "--type", "weird" },
            sw,
            er);

        await Assert.That(exit).IsNotEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Raw-режим: содержимое <c>--json-file</c> уходит в тело без трансформаций.
    /// </summary>
    [Test]
    public async Task Add_JsonFile_SendsRawBody()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var path = Path.Combine(Path.GetTempPath(), "link-add-" + Guid.NewGuid().ToString("N") + ".json");
        var raw = """{"relationship":"relates","issue":"DEV-9"}""";
        await File.WriteAllTextAsync(path, raw);

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":7}""", Encoding.UTF8, "application/json"),
            };
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "link", "add", "DEV-1", "--json-file", path },
            sw,
            er);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedBody).IsEqualTo(raw);
    }

    /// <summary>
    /// Задан только <c>--to</c>, но не <c>--type</c> — exit 2, stderr содержит
    /// <c>error.code == "invalid_args"</c>.
    /// </summary>
    [Test]
    public async Task Add_ToWithoutType_Returns_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "link", "add", "DEV-1", "--to", "DEV-2" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(2);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }
}
