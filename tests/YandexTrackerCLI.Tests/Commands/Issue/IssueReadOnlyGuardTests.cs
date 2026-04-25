using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Issue;

using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты read-only guard'а на уровне CLI-команд: проверяют, что все mutating
/// issue-команды (<c>create</c>/<c>update</c>/<c>transition --to</c>/<c>move</c>/<c>delete</c>/<c>batch</c>)
/// блокируются <see cref="YandexTrackerCLI.Core.Http.ReadOnlyGuardHandler"/>, когда профиль
/// помечен <c>read_only:true</c> или задан глобальный флаг <c>--read-only</c>. Ожидаемое поведение:
/// exit-code <c>3</c>, stderr — структурированный JSON с <c>error.code = "read_only_mode"</c>.
/// Дополнительно: GET-операции (например, <c>transition --list</c>) не блокируются.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class IssueReadOnlyGuardTests
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
    /// <c>issue create</c> в read-only-профиле — блокируется, HTTP не отправляется.
    /// </summary>
    [Test]
    public async Task IssueCreate_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "issue", "create", "--queue", "DEV", "--summary", "x" },
            sw,
            er);
        await AssertReadOnlyExit(exit, er);
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }

    /// <summary>
    /// <c>issue update</c> в read-only-профиле — блокируется.
    /// </summary>
    [Test]
    public async Task IssueUpdate_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        env.InnerHandler = new TestHttpMessageHandler();
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "update", "DEV-1", "--summary", "x" }, sw, er);
        await AssertReadOnlyExit(exit, er);
    }

    /// <summary>
    /// <c>issue transition --to</c> в read-only-профиле — блокируется (POST /_execute).
    /// </summary>
    [Test]
    public async Task IssueTransitionExecute_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        env.InnerHandler = new TestHttpMessageHandler();
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "transition", "DEV-1", "--to", "close" }, sw, er);
        await AssertReadOnlyExit(exit, er);
    }

    /// <summary>
    /// <c>issue move</c> в read-only-профиле — блокируется (POST /_move).
    /// </summary>
    [Test]
    public async Task IssueMove_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        env.InnerHandler = new TestHttpMessageHandler();
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "move", "DEV-1", "--to-queue", "NEW" }, sw, er);
        await AssertReadOnlyExit(exit, er);
    }

    /// <summary>
    /// <c>issue delete</c> в read-only-профиле — блокируется.
    /// </summary>
    [Test]
    public async Task IssueDelete_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        env.InnerHandler = new TestHttpMessageHandler();
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "delete", "DEV-1" }, sw, er);
        await AssertReadOnlyExit(exit, er);
    }

    /// <summary>
    /// <c>issue batch</c> в read-only-профиле — блокируется (POST /bulkchange).
    /// </summary>
    [Test]
    public async Task IssueBatch_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var path = Path.Combine(Path.GetTempPath(), "b-" + Guid.NewGuid() + ".json");
        await File.WriteAllTextAsync(path, """{"operations":[]}""");
        env.InnerHandler = new TestHttpMessageHandler();
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "batch", "--json-file", path }, sw, er);
        await AssertReadOnlyExit(exit, er);
    }

    /// <summary>
    /// Флаг <c>--read-only</c> поверх профиля с <c>read_only:false</c> — guard всё равно
    /// блокирует mutating запрос. Подтверждает, что CLI-флаг корректно прокидывается в policy.
    /// </summary>
    [Test]
    public async Task IssueCreate_ReadOnlyFromCliFlag_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        env.InnerHandler = new TestHttpMessageHandler();
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "--read-only", "issue", "create", "--queue", "DEV", "--summary", "x" },
            sw,
            er);
        await AssertReadOnlyExit(exit, er);
    }

    /// <summary>
    /// <c>issue find</c> использует <c>POST /v3/issues/_search</c>, но это read-only
    /// search-операция. Guard должен пропустить запрос даже в read-only-профиле.
    /// </summary>
    [Test]
    public async Task IssueFind_InReadOnlyMode_IsAllowed()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "1");
            r.Content = new StringContent("""[{"key":"DEV-1"}]""", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "find", "--queue", "DEV" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
    }

    /// <summary>
    /// GET-операции (например, <c>issue transition --list</c>) не mutating и должны проходить
    /// даже в read-only-профиле. Exit = 0.
    /// </summary>
    [Test]
    public async Task IssueTransitionList_ReadOnly_NotBlocked()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
            return r;
        });
        env.InnerHandler = inner;
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "transition", "DEV-1", "--list" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
    }
}
