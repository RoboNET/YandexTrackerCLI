using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Project;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты read-only guard'а на уровне CLI-команд группы <c>project</c>:
/// проверяют, что mutating-команды (<c>create</c>/<c>update</c>/<c>delete</c>)
/// блокируются <see cref="YandexTrackerCLI.Core.Http.ReadOnlyGuardHandler"/> при
/// <c>read_only:true</c> — exit 3, stderr JSON с <c>error.code = "read_only_mode"</c>.
/// Дополнительно: <c>project list</c> использует <c>POST /.../_search</c>, который
/// считается read-only (search — не mutating) и должен проходить под read-only политикой;
/// <c>project get</c> — GET и тоже разрешён. Мутируют глобальное state
/// (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ProjectReadOnlyGuardTests
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
    /// <c>project create</c> в read-only-профиле — блокируется, HTTP не отправляется.
    /// </summary>
    [Test]
    public async Task ProjectCreate_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var path = Path.Combine(Path.GetTempPath(), "project-create-ro-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, """{"name":"X"}""");

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "project", "create", "--json-file", path },
            sw,
            er);
        await AssertReadOnlyExit(exit, er);
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }

    /// <summary>
    /// <c>project update</c> в read-only-профиле — блокируется, HTTP не отправляется.
    /// </summary>
    [Test]
    public async Task ProjectUpdate_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var path = Path.Combine(Path.GetTempPath(), "project-update-ro-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, """{"name":"X"}""");

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "project", "update", "42", "--json-file", path },
            sw,
            er);
        await AssertReadOnlyExit(exit, er);
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }

    /// <summary>
    /// <c>project delete</c> в read-only-профиле — блокируется, HTTP не отправляется.
    /// </summary>
    [Test]
    public async Task ProjectDelete_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "project", "delete", "42" }, sw, er);
        await AssertReadOnlyExit(exit, er);
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }

    /// <summary>
    /// <c>project list</c> использует <c>POST /entities/project/_search</c>, но это
    /// семантически read-only операция (<see cref="YandexTrackerCLI.Core.Http.ReadOnlyGuardHandler"/>
    /// пропускает POST с путём <c>_search</c>) — команда должна проходить под read-only
    /// политикой с exit 0.
    /// </summary>
    [Test]
    public async Task ProjectList_ReadOnlyProfile_Allowed()
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
        var exit = await env.Invoke(new[] { "project", "list" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
    }

    /// <summary>
    /// <c>project get</c> — GET и должен проходить под read-only политикой с exit 0.
    /// </summary>
    [Test]
    public async Task ProjectGet_ReadOnlyProfile_Allowed()
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
        var exit = await env.Invoke(new[] { "project", "get", "1" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
    }
}
