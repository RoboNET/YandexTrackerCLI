using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Version;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты read-only guard'а на уровне CLI-команд группы <c>version</c>:
/// проверяют, что mutating-команды (<c>create</c>/<c>update</c>/<c>delete</c>)
/// блокируются <see cref="YandexTrackerCLI.Core.Http.ReadOnlyGuardHandler"/> при
/// <c>read_only:true</c> — exit 3, stderr JSON с <c>error.code = "read_only_mode"</c>.
/// Дополнительно: <c>version list</c> (GET) и <c>version get</c> (GET) разрешены.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class VersionReadOnlyGuardTests
{
    /// <summary>
    /// Профиль с <c>read_only:true</c> — guard должен срабатывать на все mutating операции.
    /// </summary>
    private const string ReadOnlyConfig =
        """{"default_profile":"ro","profiles":{"ro":{"org_type":"cloud","org_id":"o","read_only":true,"auth":{"type":"oauth","token":"y0_X"}}}}""";

    /// <summary>
    /// Проверяет, что команда вернула exit 3 и stderr содержит JSON с
    /// <c>error.code = "read_only_mode"</c>.
    /// </summary>
    /// <param name="exit">Exit-code команды.</param>
    /// <param name="stderr">Содержимое перехваченного stderr.</param>
    /// <returns>Task, завершающийся после выполнения всех ассершнов.</returns>
    private static async Task AssertReadOnlyExit(int exit, StringWriter stderr)
    {
        await Assert.That(exit).IsEqualTo(3);
        using var doc = JsonDocument.Parse(stderr.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("read_only_mode");
    }

    /// <summary>
    /// <c>version create</c> (POST /versions, typed) в read-only-профиле —
    /// блокируется, HTTP не отправляется.
    /// </summary>
    [Test]
    public async Task VersionCreate_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "version", "create", "--queue", "DEV", "--name", "v1.0" },
            sw,
            er);
        await AssertReadOnlyExit(exit, er);
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }

    /// <summary>
    /// <c>version update</c> (PATCH /versions/{id}, typed) в read-only-профиле —
    /// блокируется, HTTP не отправляется.
    /// </summary>
    [Test]
    public async Task VersionUpdate_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "version", "update", "42", "--name", "X" },
            sw,
            er);
        await AssertReadOnlyExit(exit, er);
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }

    /// <summary>
    /// <c>version delete</c> (DELETE /versions/{id}) в read-only-профиле —
    /// блокируется, HTTP не отправляется.
    /// </summary>
    [Test]
    public async Task VersionDelete_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "version", "delete", "42" }, sw, er);
        await AssertReadOnlyExit(exit, er);
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }

    /// <summary>
    /// <c>version list</c> — GET на <c>/queues/{queue}/versions</c>, должен
    /// проходить под read-only политикой с exit 0.
    /// </summary>
    [Test]
    public async Task VersionList_ReadOnlyProfile_Allowed()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
            return r;
        });
        env.InnerHandler = inner;
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "version", "list", "--queue", "DEV" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
    }

    /// <summary>
    /// <c>version get</c> — GET и должен проходить под read-only политикой с exit 0.
    /// </summary>
    [Test]
    public async Task VersionGet_ReadOnlyProfile_Allowed()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":1}""", Encoding.UTF8, "application/json"),
            };
            return r;
        });
        env.InnerHandler = inner;
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "version", "get", "1" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
    }
}
