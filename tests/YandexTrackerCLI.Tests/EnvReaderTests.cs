namespace YandexTrackerCLI.Tests;

using TUnit.Core;
using YandexTrackerCLI.Output;

/// <summary>
/// Тесты <see cref="EnvReader"/>: снапшот должен содержать ровно те переменные, которые
/// кто-то читает. Ключ, которого нет в снапшоте, тихо не работает; ключ, который никто не
/// читает, — мусор, обещающий пользователю несуществующее поведение.
/// Мутируют глобальное состояние (env), поэтому исполняются последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class EnvReaderTests
{
    /// <summary>
    /// <c>YT_LOG_FILE</c>/<c>YT_LOG_RAW</c> читает <see cref="TrackerContextFactory"/> из
    /// снапшота — без них в <c>Keys</c> wire-log работал только у auth-команд, которые лезут
    /// в <see cref="Environment"/> напрямую.
    /// </summary>
    [Test]
    [Arguments("YT_LOG_FILE")]
    [Arguments("YT_LOG_RAW")]
    public async Task Snapshot_ContainsWireLogKeys(string key)
    {
        using var env = new TestEnv();
        env.Set(key, "value");

        var snapshot = EnvReader.Snapshot();

        await Assert.That(snapshot.ContainsKey(key)).IsTrue();
        await Assert.That(snapshot[key]).IsEqualTo("value");
    }

    /// <summary>
    /// <c>YT_NO_COLOR</c> удалена из <c>Keys</c> как мёртвая: её никто не читал, а цвет
    /// выключают <c>NO_COLOR</c> (стандарт no-color.org), <c>--no-color</c>, <c>TERM=dumb</c>
    /// и редирект stdout. Вторая переменная с тем же смыслом только вводила в заблуждение.
    /// </summary>
    [Test]
    public async Task Snapshot_DoesNotCarry_DeadYtNoColorKey()
    {
        using var env = new TestEnv();
        env.Set("YT_NO_COLOR", "1");

        var snapshot = EnvReader.Snapshot();

        await Assert.That(snapshot.ContainsKey("YT_NO_COLOR")).IsFalse();
    }

    /// <summary>
    /// Поведение подтверждается и на резолве возможностей терминала: <c>YT_NO_COLOR</c>
    /// цвет не выключает, <c>NO_COLOR</c> — выключает.
    /// </summary>
    [Test]
    public async Task YtNoColor_DoesNotDisableColor_ButNoColorDoes()
    {
        using var env = new TestEnv();
        env.Set("NO_COLOR", null);
        env.Set("TERM", "xterm-256color");
        env.Set("YT_NO_COLOR", "1");

        var withYtNoColor = TerminalCapabilities.Detect(
            EnvReader.Snapshot(),
            noColorFlag: false,
            noPagerFlag: false,
            isOutputRedirected: () => false,
            consoleWidth: () => 100);
        await Assert.That(withYtNoColor.UseColor).IsTrue();

        env.Set("NO_COLOR", "1");
        var withStandardNoColor = TerminalCapabilities.Detect(
            EnvReader.Snapshot(),
            noColorFlag: false,
            noPagerFlag: false,
            isOutputRedirected: () => false,
            consoleWidth: () => 100);
        await Assert.That(withStandardNoColor.UseColor).IsFalse();
    }
}
