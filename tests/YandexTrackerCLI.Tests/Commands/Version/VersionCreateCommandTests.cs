using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Version;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt version create</c>. Поддерживаются два режима:
/// <b>typed</b> (<c>--queue</c>/<c>--name</c>[/<c>--description</c>/<c>--start-date</c>/<c>--due-date</c>/<c>--released</c>])
/// и <b>raw</b> (<c>--json-file</c>). Даты валидируются как ISO 8601.
/// Одновременное указание typed и raw — ошибка <c>invalid_args</c>.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class VersionCreateCommandTests
{
    /// <summary>
    /// Typed-режим с полным набором (queue/name/description/start-date/due-date/released=false):
    /// <c>queue</c> оборачивается в объект <c>{"key":...}</c>, остальные поля — плоские.
    /// URL заканчивается на <c>/versions</c>, метод POST.
    /// </summary>
    [Test]
    public async Task Create_TypedFullArgs_BuildsCompleteBody()
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
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":42}""", Encoding.UTF8, "application/json"),
            };
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[]
            {
                "version", "create",
                "--queue", "DEV",
                "--name", "v1.0",
                "--description", "First release",
                "--start-date", "2024-01-01",
                "--due-date", "2024-12-31",
                "--released", "false",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Post);
        await Assert.That(capturedPath!.EndsWith("/versions", StringComparison.Ordinal)).IsTrue();

        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("queue").GetProperty("key").GetString()).IsEqualTo("DEV");
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("v1.0");
        await Assert.That(doc.RootElement.GetProperty("description").GetString()).IsEqualTo("First release");
        await Assert.That(doc.RootElement.GetProperty("startDate").GetString()).IsEqualTo("2024-01-01");
        await Assert.That(doc.RootElement.GetProperty("dueDate").GetString()).IsEqualTo("2024-12-31");
        await Assert.That(doc.RootElement.GetProperty("released").GetBoolean()).IsFalse();
    }

    /// <summary>
    /// Typed-режим с минимальным набором (только <c>--queue</c>/<c>--name</c>):
    /// в теле только <c>queue</c> и <c>name</c>; опциональные поля отсутствуют.
    /// </summary>
    [Test]
    public async Task Create_TypedMinimal_OmitsOptionalFields()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":42}""", Encoding.UTF8, "application/json"),
            };
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "version", "create", "--queue", "DEV", "--name", "v1.0" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("queue").GetProperty("key").GetString()).IsEqualTo("DEV");
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("v1.0");
        await Assert.That(doc.RootElement.TryGetProperty("description", out _)).IsFalse();
        await Assert.That(doc.RootElement.TryGetProperty("startDate", out _)).IsFalse();
        await Assert.That(doc.RootElement.TryGetProperty("dueDate", out _)).IsFalse();
        await Assert.That(doc.RootElement.TryGetProperty("released", out _)).IsFalse();
    }

    /// <summary>
    /// Raw-режим: содержимое <c>--json-file</c> уходит в тело POST без изменений.
    /// </summary>
    [Test]
    public async Task Create_JsonFile_PostsRawBody()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var path = Path.Combine(Path.GetTempPath(), "version-create-" + Guid.NewGuid().ToString("N") + ".json");
        var raw = """{"queue":{"key":"DEV"},"name":"Raw"}""";
        await File.WriteAllTextAsync(path, raw);

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":42}""", Encoding.UTF8, "application/json"),
            };
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "version", "create", "--json-file", path },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedBody).IsEqualTo(raw);
    }

    /// <summary>
    /// Невалидная <c>--start-date</c> → exit 2, stderr содержит
    /// <c>error.code == "invalid_args"</c>, HTTP не вызывается.
    /// </summary>
    [Test]
    public async Task Create_InvalidStartDate_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[]
            {
                "version", "create",
                "--queue", "DEV",
                "--name", "v1.0",
                "--start-date", "not-a-date",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(2);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Typed-флаг + <c>--json-file</c> одновременно → exit 2, stderr содержит
    /// <c>error.code == "invalid_args"</c>, HTTP не вызывается.
    /// </summary>
    [Test]
    public async Task Create_TypedAndRawTogether_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var path = Path.Combine(Path.GetTempPath(), "version-create-conflict-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, """{"name":"X"}""");

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "version", "create", "--name", "X", "--json-file", path },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(2);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }
}
