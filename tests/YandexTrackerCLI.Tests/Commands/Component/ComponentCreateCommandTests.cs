using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Component;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt component create</c>. Поддерживаются два режима:
/// <b>typed</b> (<c>--queue</c>/<c>--name</c>[/<c>--description</c>/<c>--lead</c>/<c>--assign-auto</c>])
/// и <b>raw</b> (<c>--json-file</c>). Одновременное указание typed и raw — ошибка
/// <c>invalid_args</c>. Мутируют глобальное state (env + Console + AsyncLocal),
/// поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ComponentCreateCommandTests
{
    /// <summary>
    /// Typed-режим с минимальным набором (<c>--queue</c>/<c>--name</c>): <c>queue</c>
    /// оборачивается в объект <c>{"key":...}</c>, <c>name</c> — простая строка. URL
    /// заканчивается на <c>/components</c>, метод POST.
    /// </summary>
    [Test]
    public async Task Create_TypedQueueAndName_BuildsWrappedBody()
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
            new[] { "component", "create", "--queue", "DEV", "--name", "API" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Post);
        await Assert.That(capturedPath!.EndsWith("/components", StringComparison.Ordinal)).IsTrue();

        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("queue").GetProperty("key").GetString()).IsEqualTo("DEV");
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("API");
        await Assert.That(doc.RootElement.TryGetProperty("description", out _)).IsFalse();
        await Assert.That(doc.RootElement.TryGetProperty("lead", out _)).IsFalse();
        await Assert.That(doc.RootElement.TryGetProperty("assignAuto", out _)).IsFalse();
    }

    /// <summary>
    /// Typed-режим с <c>--lead</c> и <c>--assign-auto</c>: поле <c>lead</c>
    /// оборачивается в объект <c>{"login":...}</c>, <c>assignAuto</c> — булево <c>true</c>.
    /// </summary>
    [Test]
    public async Task Create_TypedWithLeadAndAssignAuto_WrapsLeadObject()
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
            new[]
            {
                "component", "create",
                "--queue", "DEV",
                "--name", "Infra",
                "--description", "Infrastructure",
                "--lead", "john",
                "--assign-auto",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("queue").GetProperty("key").GetString()).IsEqualTo("DEV");
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("Infra");
        await Assert.That(doc.RootElement.GetProperty("description").GetString()).IsEqualTo("Infrastructure");
        await Assert.That(doc.RootElement.GetProperty("lead").GetProperty("login").GetString()).IsEqualTo("john");
        await Assert.That(doc.RootElement.GetProperty("assignAuto").GetBoolean()).IsTrue();
    }

    /// <summary>
    /// Raw-режим: содержимое <c>--json-file</c> уходит в тело POST без трансформаций.
    /// </summary>
    [Test]
    public async Task Create_JsonFile_PostsRawBody()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var path = Path.Combine(Path.GetTempPath(), "component-create-" + Guid.NewGuid().ToString("N") + ".json");
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
            new[] { "component", "create", "--json-file", path },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("queue").GetProperty("key").GetString()).IsEqualTo("DEV");
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("Raw");
    }

    /// <summary>
    /// Merge: <c>--json-file</c> + scalar inline <c>--name</c>/<c>--description</c> => override побеждает.
    /// </summary>
    [Test]
    public async Task Create_JsonFile_WithInlineOverride_Merges()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var path = Path.Combine(Path.GetTempPath(), "component-create-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, """{"queue":{"key":"DEV"},"name":"old","description":"old desc"}""");

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
            new[] { "component", "create", "--json-file", path, "--name", "new", "--assign-auto" },
            sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("queue").GetProperty("key").GetString()).IsEqualTo("DEV");
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("new");
        await Assert.That(doc.RootElement.GetProperty("description").GetString()).IsEqualTo("old desc");
        await Assert.That(doc.RootElement.GetProperty("assignAuto").GetBoolean()).IsTrue();
    }

    /// <summary>
    /// Nested-typed (<c>--queue</c>) + raw одновременно → exit 2.
    /// </summary>
    [Test]
    public async Task Create_NestedTypedAndRawTogether_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var path = Path.Combine(Path.GetTempPath(), "component-create-conflict-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, """{"name":"X"}""");

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "component", "create", "--queue", "DEV", "--json-file", path },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(2);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }
}
