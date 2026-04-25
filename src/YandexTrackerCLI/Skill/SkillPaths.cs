namespace YandexTrackerCLI.Skill;

/// <summary>
/// Резолвер путей до файлов skill'а в Claude и Codex для каждой комбинации
/// <see cref="SkillTarget"/> × <see cref="SkillScope"/>.
/// </summary>
public static class SkillPaths
{
    /// <summary>
    /// Возвращает путь, по которому будет создан/обновлён файл skill'а.
    /// </summary>
    /// <param name="target">Целевой ассистент.</param>
    /// <param name="scope">Зона установки.</param>
    /// <param name="projectDir">Корень проекта; используется только для <see cref="SkillScope.Project"/>.
    /// Должен быть абсолютным.</param>
    /// <returns>Полный путь до файла (для Claude — <c>SKILL.md</c>; для Codex — <c>AGENTS.md</c>).</returns>
    /// <exception cref="ArgumentException">Если <paramref name="projectDir"/> не задан для <see cref="SkillScope.Project"/>.</exception>
    public static string Resolve(SkillTarget target, SkillScope scope, string? projectDir)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Claude:  <base>/.claude/skills/yt/SKILL.md
        // Codex:   <base>/.agents/skills/yt/SKILL.md  (https://developers.openai.com/codex/skills)
        var rootDirName = target switch
        {
            SkillTarget.Claude => ".claude",
            SkillTarget.Codex => ".agents",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown skill target."),
        };

        var baseDir = scope switch
        {
            SkillScope.Global => home,
            SkillScope.Project => EnsureProject(projectDir),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown skill scope."),
        };

        return Path.Combine(baseDir, rootDirName, "skills", "yt", "SKILL.md");
    }

    /// <summary>
    /// Каталог установленного state-файла auto-check ("declined", "never_prompt").
    /// На POSIX — <c>~/.cache/yandex-tracker/</c>; на Windows — <c>%LOCALAPPDATA%\yandex-tracker\</c>.
    /// </summary>
    /// <returns>Абсолютный путь до файла <c>skill-prompt-state.json</c>.</returns>
    public static string PromptStateFile()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var baseDir = !string.IsNullOrEmpty(xdgCache)
            ? xdgCache
            : OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : Path.Combine(home, ".cache");
        return Path.Combine(baseDir, "yandex-tracker", "skill-prompt-state.json");
    }

    private static string EnsureProject(string? projectDir)
    {
        if (string.IsNullOrWhiteSpace(projectDir))
        {
            throw new ArgumentException("projectDir is required for SkillScope.Project.", nameof(projectDir));
        }
        return Path.GetFullPath(projectDir);
    }
}
