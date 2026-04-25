using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Worklog;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты read-only guard'а на уровне CLI-команд: проверяют, что все mutating
/// worklog-команды (<c>add</c>/<c>update</c>/<c>delete</c>) блокируются
/// <see cref="YandexTrackerCLI.Core.Http.ReadOnlyGuardHandler"/>, когда профиль помечен
/// <c>read_only:true</c>. Ожидаемое поведение: exit-code <c>3</c>, stderr — структурированный
/// JSON с <c>error.code = "read_only_mode"</c>. Дополнительно: GET-операции (например,
/// <c>worklog list</c>) не блокируются. Мутируют глобальное state (env + Console + AsyncLocal),
/// поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class WorklogReadOnlyGuardTests
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
    /// <c>worklog add</c> в read-only-профиле — блокируется, HTTP не отправляется.
    /// </summary>
    [Test]
    public async Task WorklogAdd_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "worklog", "add", "DEV-1", "--duration", "PT1H" },
            sw,
            er);
        await AssertReadOnlyExit(exit, er);
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }

    /// <summary>
    /// <c>worklog update</c> в read-only-профиле — блокируется.
    /// </summary>
    [Test]
    public async Task WorklogUpdate_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        env.InnerHandler = new TestHttpMessageHandler();
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "worklog", "update", "DEV-1", "42", "--comment", "upd" },
            sw,
            er);
        await AssertReadOnlyExit(exit, er);
    }

    /// <summary>
    /// <c>worklog delete</c> в read-only-профиле — блокируется.
    /// </summary>
    [Test]
    public async Task WorklogDelete_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        env.InnerHandler = new TestHttpMessageHandler();
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "worklog", "delete", "DEV-1", "42" },
            sw,
            er);
        await AssertReadOnlyExit(exit, er);
    }

    /// <summary>
    /// GET-операция <c>worklog list</c> не mutating и должна проходить даже в
    /// read-only-профиле. Exit = 0.
    /// </summary>
    [Test]
    public async Task WorklogList_ReadOnlyProfile_NotBlocked()
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
        var exit = await env.Invoke(new[] { "worklog", "list", "DEV-1" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
    }
}
