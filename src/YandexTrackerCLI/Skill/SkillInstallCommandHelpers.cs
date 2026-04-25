namespace YandexTrackerCLI.Skill;

/// <summary>
/// Чистые helper'ы для команды <c>yt skill install</c>: определение интерактивного режима
/// и список существующих target-файлов для подтверждения <c>--force</c>. Вынесены из
/// <see cref="SkillManager"/> и из самой команды, чтобы тестировать независимо.
/// </summary>
public static class SkillInstallCommandHelpers
{
    /// <summary>
    /// Test-hook для подмены TTY-detection. <c>null</c> — использовать
    /// <see cref="Console.IsInputRedirected"/>/<see cref="Console.IsOutputRedirected"/>.
    /// </summary>
    public static readonly AsyncLocal<bool?> TestForceInteractive = new();

    /// <summary>
    /// Решает, нужно ли запустить интерактивный prompt. <c>true</c>, если выполнены
    /// все условия:
    /// <list type="bullet">
    ///   <item><description>Это TTY (или <see cref="TestForceInteractive"/> = <c>true</c>).</description></item>
    ///   <item><description>Пользователь НЕ передал <c>--no-prompt</c>.</description></item>
    ///   <item><description>Пользователь НЕ передал явные <c>--target</c>/<c>--scope</c>.</description></item>
    /// </list>
    /// </summary>
    /// <param name="userPassedTarget">Передан ли явно <c>--target</c>.</param>
    /// <param name="userPassedScope">Передан ли явно <c>--scope</c>.</param>
    /// <param name="noPrompt">Передан ли флаг <c>--no-prompt</c>.</param>
    /// <returns><c>true</c>, если стоит запустить интерактивный prompt.</returns>
    public static bool ShouldRunInteractive(bool userPassedTarget, bool userPassedScope, bool noPrompt)
    {
        if (noPrompt)
        {
            return false;
        }
        if (userPassedTarget || userPassedScope)
        {
            return false;
        }
        return IsInteractive();
    }

    /// <summary>
    /// <c>true</c>, если CLI запущена в TTY. Уважает <see cref="TestForceInteractive"/>.
    /// </summary>
    public static bool IsInteractive()
    {
        if (TestForceInteractive.Value is { } forced)
        {
            return forced;
        }
        return !Console.IsInputRedirected && !Console.IsOutputRedirected;
    }

    /// <summary>
    /// Возвращает список путей, которые уже существуют для запрошенных target × scope
    /// и будут перезаписаны при установке. Используется для prompt'а подтверждения.
    /// </summary>
    /// <param name="targets">Выбранные target'ы.</param>
    /// <param name="scope">Выбранный scope.</param>
    /// <param name="projectDir">Корень проекта (для project-scope и Copilot).</param>
    /// <returns>Список существующих файлов; пустой, если ничего перезаписывать не нужно.</returns>
    public static IReadOnlyList<string> CollectExistingPaths(
        IReadOnlyList<SkillTarget> targets, SkillScope scope, string projectDir)
    {
        var existing = new List<string>();
        foreach (var t in targets)
        {
            string path;
            try
            {
                path = SkillPaths.Resolve(t, scope, projectDir);
            }
            catch (NotSupportedException)
            {
                // Copilot+Global — не поддерживается, нечего перезаписывать.
                continue;
            }
            if (File.Exists(path))
            {
                existing.Add(path);
            }
        }
        return existing;
    }
}
