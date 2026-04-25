using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Checklist;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты read-only guard'а на уровне CLI-команд: проверяют, что все mutating
/// checklist-команды (<c>add-item</c>/<c>toggle</c>/<c>update</c>/<c>remove</c>)
/// блокируются <see cref="YandexTrackerCLI.Core.Http.ReadOnlyGuardHandler"/>, когда профиль
/// помечен <c>read_only:true</c>. Ожидаемое поведение: exit-code <c>3</c>, stderr —
/// структурированный JSON с <c>error.code = "read_only_mode"</c>. Дополнительно: GET-операция
/// <c>checklist get</c> не блокируется. Мутируют глобальное state
/// (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ChecklistReadOnlyGuardTests
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
    /// <c>checklist add-item</c> в read-only-профиле — блокируется, HTTP не отправляется.
    /// </summary>
    [Test]
    public async Task ChecklistAddItem_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "checklist", "add-item", "DEV-1", "--text", "t" },
            sw,
            er);
        await AssertReadOnlyExit(exit, er);
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }

    /// <summary>
    /// <c>checklist toggle</c> с <c>--checked</c> в read-only-профиле — сразу блокируется
    /// на PATCH, никаких HTTP-запросов (GET не выполнялся, потому что значение задано явно).
    /// </summary>
    [Test]
    public async Task ChecklistToggle_Explicit_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "checklist", "toggle", "DEV-1", "i1", "--checked", "true" },
            sw,
            er);
        await AssertReadOnlyExit(exit, er);
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }

    /// <summary>
    /// <c>checklist toggle</c> без <c>--checked</c> в read-only-профиле: GET списка
    /// проходит (read-only не блокирует GET), но последующий PATCH блокируется guard'ом.
    /// </summary>
    [Test]
    public async Task ChecklistToggle_AutoInvert_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);

        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"id":"i1","checked":false}]""",
                    Encoding.UTF8,
                    "application/json"),
            };
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "checklist", "toggle", "DEV-1", "i1" }, sw, er);
        await AssertReadOnlyExit(exit, er);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        await Assert.That(inner.Seen[0].Method).IsEqualTo(HttpMethod.Get);
    }

    /// <summary>
    /// <c>checklist update</c> в read-only-профиле — блокируется.
    /// </summary>
    [Test]
    public async Task ChecklistUpdate_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        env.InnerHandler = new TestHttpMessageHandler();
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "checklist", "update", "DEV-1", "i1", "--text", "x" },
            sw,
            er);
        await AssertReadOnlyExit(exit, er);
    }

    /// <summary>
    /// <c>checklist remove</c> в read-only-профиле — блокируется.
    /// </summary>
    [Test]
    public async Task ChecklistRemove_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        env.InnerHandler = new TestHttpMessageHandler();
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "checklist", "remove", "DEV-1", "i1" },
            sw,
            er);
        await AssertReadOnlyExit(exit, er);
    }

    /// <summary>
    /// GET-операция <c>checklist get</c> не mutating и должна проходить даже в
    /// read-only-профиле. Exit = 0.
    /// </summary>
    [Test]
    public async Task ChecklistGet_ReadOnlyProfile_NotBlocked()
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
        var exit = await env.Invoke(new[] { "checklist", "get", "DEV-1" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
    }
}
