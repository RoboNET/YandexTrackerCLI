using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Component;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt component update &lt;id&gt;</c>. Typed-режим (все поля
/// опциональны) и raw-режим (<c>--json-file</c>/<c>--json-stdin</c>). Без источника
/// данных → exit 2. Мутируют глобальное state (env + Console + AsyncLocal),
/// поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ComponentUpdateCommandTests
{
    /// <summary>
    /// Typed-режим с <c>--name</c>/<c>--lead</c>: метод PATCH, URL заканчивается на
    /// <c>/components/42</c>, в теле — только указанные поля (lead обёрнут в объект).
    /// </summary>
    [Test]
    public async Task Update_TypedFields_PatchesTargetedBody()
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
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":42,"name":"Renamed"}""", Encoding.UTF8, "application/json"),
            };
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "component", "update", "42", "--name", "Renamed", "--lead", "alice" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Patch);
        await Assert.That(capturedPath!.EndsWith("/components/42", StringComparison.Ordinal)).IsTrue();

        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("Renamed");
        await Assert.That(doc.RootElement.GetProperty("lead").GetProperty("login").GetString()).IsEqualTo("alice");
        await Assert.That(doc.RootElement.TryGetProperty("description", out _)).IsFalse();
        await Assert.That(doc.RootElement.TryGetProperty("assignAuto", out _)).IsFalse();
    }

    /// <summary>
    /// Raw-режим: содержимое <c>--json-file</c> уходит в тело PATCH без изменений.
    /// </summary>
    [Test]
    public async Task Update_JsonFile_PatchesRawBody()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var path = Path.Combine(Path.GetTempPath(), "component-update-" + Guid.NewGuid().ToString("N") + ".json");
        var raw = """{"name":"Raw","description":"d"}""";
        await File.WriteAllTextAsync(path, raw);

        string? capturedBody = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":42}""", Encoding.UTF8, "application/json"),
            };
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "component", "update", "42", "--json-file", path },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedBody).IsEqualTo(raw);
    }

    /// <summary>
    /// Без typed-флагов и без <c>--json-file</c>/<c>--json-stdin</c>: exit 2, stderr
    /// содержит <c>error.code == "invalid_args"</c>, HTTP не вызывается.
    /// </summary>
    [Test]
    public async Task Update_NothingToUpdate_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "component", "update", "42" }, sw, er);

        await Assert.That(exit).IsEqualTo(2);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }
}
