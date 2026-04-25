namespace YandexTrackerCLI.Interactive;

using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// Кросс-платформенная реализация <see cref="IBrowserLauncher"/>, открывающая
/// URL через нативную утилиту ОС: <c>open</c> (macOS), <c>xdg-open</c> (Linux),
/// <c>cmd /c start</c> (Windows).
/// </summary>
/// <remarks>
/// Намеренно не использует <c>ProcessStartInfo.UseShellExecute = true</c> — это
/// может вести себя непредсказуемо под AOT на Linux. Вместо этого все команды
/// запускаются явным <see cref="Process.Start(ProcessStartInfo)"/> с заранее
/// известным исполняемым файлом.
/// </remarks>
public sealed class SystemBrowserLauncher : IBrowserLauncher
{
    /// <inheritdoc />
    public Task OpenAsync(string url, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var psi = BuildStartInfo(url)
                  ?? throw new PlatformNotSupportedException("Unsupported platform for opening a browser URL.");
        using var process = Process.Start(psi);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Возвращает <see cref="ProcessStartInfo"/> для открытия <paramref name="url"/>
    /// или <c>null</c>, если текущая ОС не поддерживается.
    /// </summary>
    /// <param name="url">URL, который нужно открыть.</param>
    /// <returns>Сконфигурированный <see cref="ProcessStartInfo"/> либо <c>null</c>.</returns>
    internal static ProcessStartInfo? BuildStartInfo(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new ProcessStartInfo("open", url) { UseShellExecute = false };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new ProcessStartInfo("xdg-open", url) { UseShellExecute = false };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }

        return null;
    }

    /// <summary>
    /// Test hook, экспонирующий <see cref="BuildStartInfo"/> для юнит-тестов
    /// (чтобы не требовать реального запуска процесса и не менять visibility
    /// приватного метода).
    /// </summary>
    /// <param name="url">URL для проверки.</param>
    /// <returns>Результат <see cref="BuildStartInfo"/>.</returns>
    internal static ProcessStartInfo? BuildStartInfoForTests(string url) => BuildStartInfo(url);
}
