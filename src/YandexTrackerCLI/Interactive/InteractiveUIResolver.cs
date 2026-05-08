namespace YandexTrackerCLI.Interactive;

using Spectre.Console;
using YandexTrackerCLI.Output;

/// <summary>
/// Резолвит реализацию <see cref="IInteractiveUI"/> на основе формата вывода и
/// состояния stdout/stderr. JSON/Minimal и любой redirect → <see cref="NoopInteractiveUI"/>;
/// Table/Detail в TTY → <see cref="SpectreInteractiveUI"/>.
/// </summary>
/// <remarks>
/// Тесты подменяют поведение через <see cref="TestOverride"/> (AsyncLocal), по тому
/// же паттерну что <c>SkillInstallPrompt.TestOverride</c>.
/// </remarks>
public static class InteractiveUIResolver
{
    /// <summary>
    /// Test-override: AsyncLocal-фейк, форсящий конкретную реализацию. <c>null</c> —
    /// использовать обычный resolve.
    /// </summary>
    public static readonly AsyncLocal<IInteractiveUI?> TestOverride = new();

    /// <summary>
    /// Делегаты для проверки redirect — позволяют тестам форсить TTY-режим без
    /// реального терминала. По умолчанию используют <see cref="Console.IsOutputRedirected"/>
    /// и <see cref="Console.IsErrorRedirected"/>.
    /// </summary>
    public static readonly AsyncLocal<Func<bool>?> TestIsOutputRedirected = new();

    /// <summary>
    /// См. <see cref="TestIsOutputRedirected"/>.
    /// </summary>
    public static readonly AsyncLocal<Func<bool>?> TestIsErrorRedirected = new();

    /// <summary>
    /// Резолвит <see cref="IInteractiveUI"/>.
    /// </summary>
    /// <param name="format">Эффективный формат вывода (после cascade-резолва).</param>
    /// <param name="testAnsiOverride">Опциональный <see cref="IAnsiConsole"/> для тестов
    /// (если задан и format/redirect не запрещают rich UI — обернётся в Spectre).</param>
    /// <returns>Реализация UI.</returns>
    public static IInteractiveUI Resolve(OutputFormat format, IAnsiConsole? testAnsiOverride = null)
    {
        if (TestOverride.Value is { } overridden)
        {
            return overridden;
        }

        // Машиночитаемые форматы — никаких декораций, чтобы не ломать pipe consumers.
        if (format is OutputFormat.Json or OutputFormat.Minimal)
        {
            return NoopInteractiveUI.Instance;
        }

        var stdoutRedirected = (TestIsOutputRedirected.Value ?? (() => Console.IsOutputRedirected))();
        var stderrRedirected = (TestIsErrorRedirected.Value ?? (() => Console.IsErrorRedirected))();
        if (stdoutRedirected || stderrRedirected)
        {
            return NoopInteractiveUI.Instance;
        }

        if (testAnsiOverride is not null)
        {
            return new SpectreInteractiveUI(testAnsiOverride);
        }

        return SpectreInteractiveUI.CreateForStderr();
    }
}
