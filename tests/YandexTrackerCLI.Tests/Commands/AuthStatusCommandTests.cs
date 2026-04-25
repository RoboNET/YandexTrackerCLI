namespace YandexTrackerCLI.Tests.Commands;

using System.Text.Json;
using TUnit.Core;

/// <summary>
/// End-to-end тесты команды <c>yt auth status</c>.
/// Тесты мутируют глобальное состояние (<see cref="Console"/> и переменные окружения),
/// поэтому должны выполняться последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class AuthStatusCommandTests
{
    /// <summary>
    /// Пустой конфиг без профиля и без env — должна вернуться ошибка конфигурации (exit 9).
    /// </summary>
    [Test]
    public async Task AuthStatus_NoConfig_ReturnsConfigError()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "auth", "status" }, sw, er);

        await Assert.That(exit).IsEqualTo(9);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("config_error");
    }

    /// <summary>
    /// Профиль с OAuth + cloud — печатает summary и завершается с exit 0.
    /// </summary>
    [Test]
    public async Task AuthStatus_WithOAuthProfile_PrintsSummary()
    {
        using var env = new TestEnv();
        env.SetConfig("""
        {
          "default_profile":"work",
          "profiles":{"work":{"org_type":"cloud","org_id":"o","read_only":false,
                              "auth":{"type":"oauth","token":"y0_x"}}}
        }
        """);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "auth", "status" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("profile").GetString()).IsEqualTo("work");
        await Assert.That(doc.RootElement.GetProperty("auth_type").GetString()).IsEqualTo("oauth");
        await Assert.That(doc.RootElement.GetProperty("org_type").GetString()).IsEqualTo("cloud");
        await Assert.That(doc.RootElement.GetProperty("read_only").GetBoolean()).IsFalse();
    }

    /// <summary>
    /// Профиль с <c>read_only: true</c> возвращает <c>true</c> в поле <c>read_only</c>.
    /// </summary>
    [Test]
    public async Task AuthStatus_WithReadOnlyProfile_ReturnsTrue()
    {
        using var env = new TestEnv();
        env.SetConfig("""
        {"default_profile":"ro","profiles":{"ro":{"org_type":"cloud","org_id":"o","read_only":true,
          "auth":{"type":"oauth","token":"y0_x"}}}}
        """);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "auth", "status" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("read_only").GetBoolean()).IsTrue();
    }
}
