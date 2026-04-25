namespace YandexTrackerCLI.Tests.Interactive;

using TUnit.Core;
using YandexTrackerCLI.Interactive;

/// <summary>
/// Тесты для <see cref="SystemBrowserLauncher"/>. Не запускают реальный процесс,
/// чтобы тест-среды без GUI/xdg-open не падали; проверяют только корректность
/// построения <see cref="System.Diagnostics.ProcessStartInfo"/>.
/// </summary>
public sealed class SystemBrowserLauncherTests
{
    /// <summary>
    /// Для текущей поддерживаемой ОС (macOS/Linux/Windows) метод должен
    /// вернуть непустой PSI с валидным FileName.
    /// </summary>
    [Test]
    public async Task BuildStartInfo_ReturnsValidPsi_ForCurrentPlatform()
    {
        var psi = SystemBrowserLauncher.BuildStartInfoForTests("https://example.com/authorize?x=1");

        await Assert.That(psi).IsNotNull();
        await Assert.That(psi!.FileName.Length).IsGreaterThan(0);
        await Assert.That(psi.UseShellExecute).IsFalse();
    }
}
