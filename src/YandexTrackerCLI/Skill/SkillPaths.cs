namespace YandexTrackerCLI.Skill;

/// <summary>
/// Резолвер путей до файлов skill'а в Claude / Codex / Gemini / Cursor / Copilot
/// для каждой комбинации <see cref="SkillTarget"/> × <see cref="SkillScope"/>.
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
    /// <returns>Полный путь до файла:
    /// <list type="bullet">
    ///   <item><description>Claude/Codex/Gemini — <c>SKILL.md</c> в <c>&lt;base&gt;/.&lt;agent&gt;/skills/yt/</c>.</description></item>
    ///   <item><description>Cursor — <c>yt.mdc</c> прямо в <c>&lt;base&gt;/.cursor/rules/</c> (без подкаталога).</description></item>
    ///   <item><description>Copilot — <c>yt.instructions.md</c> в <c>&lt;projectDir&gt;/.github/instructions/</c> (только project-scope).</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="ArgumentException">Если <paramref name="projectDir"/> не задан для <see cref="SkillScope.Project"/>.</exception>
    /// <exception cref="NotSupportedException">Если <paramref name="target"/> = <see cref="SkillTarget.Copilot"/>
    /// и <paramref name="scope"/> = <see cref="SkillScope.Global"/> (Copilot не поддерживает global-scope).</exception>
    public static string Resolve(SkillTarget target, SkillScope scope, string? projectDir)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Copilot — единственный target без global-scope.
        if (target == SkillTarget.Copilot)
        {
            if (scope == SkillScope.Global)
            {
                throw new NotSupportedException(
                    "Copilot does not support global scope; use --scope project.");
            }
            var pdir = EnsureProject(projectDir);
            return Path.Combine(pdir, ".github", "instructions", "yt.instructions.md");
        }

        var baseDir = scope switch
        {
            SkillScope.Global => home,
            SkillScope.Project => EnsureProject(projectDir),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown skill scope."),
        };

        return target switch
        {
            // Claude / Codex / Gemini — single-file SKILL.md в .<agent>/skills/yt/.
            SkillTarget.Claude => Path.Combine(baseDir, ".claude", "skills", "yt", "SKILL.md"),
            SkillTarget.Codex => Path.Combine(baseDir, ".agents", "skills", "yt", "SKILL.md"),
            SkillTarget.Gemini => Path.Combine(baseDir, ".gemini", "skills", "yt", "SKILL.md"),
            // Cursor — yt.mdc прямо в .cursor/rules/, без yt/ подкаталога.
            SkillTarget.Cursor => Path.Combine(baseDir, ".cursor", "rules", "yt.mdc"),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown skill target."),
        };
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
