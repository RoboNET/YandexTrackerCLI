namespace YandexTrackerCLI.Skill;

using System.Runtime.InteropServices;
using Core.Api.Errors;

/// <summary>
/// Высокоуровневые операции над skill'ом: install/uninstall/status/update в
/// Claude Code, OpenAI Codex, Gemini CLI, Cursor и GitHub Copilot — глобально или per-project.
/// </summary>
/// <remarks>
/// Логика установки одинаковая для всех target'ов, разница только в:
/// <list type="bullet">
///   <item><description>Базовом пути и имени файла (см. <see cref="SkillPaths"/>).</description></item>
///   <item><description>Формате контента: Claude/Codex/Gemini получают полный SKILL.md as-is,
///         Cursor — <c>.mdc</c> с другим frontmatter, Copilot — <c>.instructions.md</c> с <c>applyTo</c>.</description></item>
/// </list>
/// Версия установленной локации детектится через единый маркер <c>&lt;!-- yt-version: X.Y.Z --&gt;</c>
/// в теле файла — он присутствует во всех вариантах.
/// </remarks>
public static class SkillManager
{
    /// <summary>
    /// Описывает одну установленную локацию skill'а.
    /// </summary>
    /// <param name="Target">Целевой ассистент.</param>
    /// <param name="Scope">Зона.</param>
    /// <param name="Path">Полный путь до файла на диске.</param>
    /// <param name="Version">Извлечённая из установленного файла версия (или <c>"unknown"</c>).</param>
    public sealed record Installation(SkillTarget Target, SkillScope Scope, string Path, string Version);

    /// <summary>
    /// Результат установки/обновления одной локации.
    /// </summary>
    /// <param name="Target">Целевой ассистент.</param>
    /// <param name="Scope">Зона.</param>
    /// <param name="Path">Путь до записанного файла.</param>
    /// <param name="Version">Версия, которая была записана (актуальная).</param>
    /// <param name="FromVersion">Предыдущая установленная версия (только для update).</param>
    public sealed record InstallResult(SkillTarget Target, SkillScope Scope, string Path, string Version, string? FromVersion = null);

    /// <summary>
    /// Описывает локацию, которая была пропущена при установке (например, Copilot+global).
    /// </summary>
    /// <param name="Target">Целевой ассистент.</param>
    /// <param name="Scope">Запрошенная зона.</param>
    /// <param name="Reason">Человеко-читаемая причина пропуска.</param>
    public sealed record SkippedInstall(SkillTarget Target, SkillScope Scope, string Reason);

    /// <summary>
    /// Текущий статус установленных локаций.
    /// </summary>
    /// <param name="CurrentVersion">Версия бинаря.</param>
    /// <param name="ClaudeGlobal">Текущая Claude/global установка или <c>null</c>.</param>
    /// <param name="ClaudeProject">Текущая Claude/project установка или <c>null</c>.</param>
    /// <param name="CodexGlobal">Текущая Codex/global установка или <c>null</c>.</param>
    /// <param name="CodexProject">Текущая Codex/project установка или <c>null</c>.</param>
    /// <param name="GeminiGlobal">Текущая Gemini/global установка или <c>null</c>.</param>
    /// <param name="GeminiProject">Текущая Gemini/project установка или <c>null</c>.</param>
    /// <param name="CursorGlobal">Текущая Cursor/global установка или <c>null</c>.</param>
    /// <param name="CursorProject">Текущая Cursor/project установка или <c>null</c>.</param>
    /// <param name="CopilotProject">Текущая Copilot/project установка или <c>null</c>.
    /// Copilot не имеет global-варианта, поэтому соответствующее поле отсутствует.</param>
    public sealed record Status(
        string CurrentVersion,
        Installation? ClaudeGlobal,
        Installation? ClaudeProject,
        Installation? CodexGlobal,
        Installation? CodexProject,
        Installation? GeminiGlobal,
        Installation? GeminiProject,
        Installation? CursorGlobal,
        Installation? CursorProject,
        Installation? CopilotProject)
    {
        /// <summary>
        /// Перечисляет все установленные локации (исключая <c>null</c>).
        /// </summary>
        public IEnumerable<Installation> All()
        {
            if (ClaudeGlobal is not null) yield return ClaudeGlobal;
            if (ClaudeProject is not null) yield return ClaudeProject;
            if (CodexGlobal is not null) yield return CodexGlobal;
            if (CodexProject is not null) yield return CodexProject;
            if (GeminiGlobal is not null) yield return GeminiGlobal;
            if (GeminiProject is not null) yield return GeminiProject;
            if (CursorGlobal is not null) yield return CursorGlobal;
            if (CursorProject is not null) yield return CursorProject;
            if (CopilotProject is not null) yield return CopilotProject;
        }

        /// <summary>
        /// Возвращает локации, у которых версия не совпадает с <see cref="CurrentVersion"/>.
        /// </summary>
        public IEnumerable<Installation> Outdated() =>
            All().Where(i => !string.Equals(i.Version, CurrentVersion, StringComparison.Ordinal));

        /// <summary>
        /// <c>true</c> если хоть одна локация имеет несовпадение с актуальной версией.
        /// </summary>
        public bool AnyOutdated => Outdated().Any();
    }

    /// <summary>
    /// Возвращает текущий статус всех возможных локаций (Claude/Codex/Gemini/Cursor × Global/Project,
    /// плюс Copilot/Project).
    /// </summary>
    /// <param name="projectDir">Корень проекта (для project-scope).</param>
    /// <returns>Снимок состояния.</returns>
    public static Status GetStatus(string projectDir) =>
        new(
            EmbeddedSkill.GetVersion(),
            ClaudeGlobal: Probe(SkillTarget.Claude, SkillScope.Global, projectDir),
            ClaudeProject: Probe(SkillTarget.Claude, SkillScope.Project, projectDir),
            CodexGlobal: Probe(SkillTarget.Codex, SkillScope.Global, projectDir),
            CodexProject: Probe(SkillTarget.Codex, SkillScope.Project, projectDir),
            GeminiGlobal: Probe(SkillTarget.Gemini, SkillScope.Global, projectDir),
            GeminiProject: Probe(SkillTarget.Gemini, SkillScope.Project, projectDir),
            CursorGlobal: Probe(SkillTarget.Cursor, SkillScope.Global, projectDir),
            CursorProject: Probe(SkillTarget.Cursor, SkillScope.Project, projectDir),
            // Copilot — только project-scope.
            CopilotProject: Probe(SkillTarget.Copilot, SkillScope.Project, projectDir));

    /// <summary>
    /// Устанавливает skill в указанную локацию.
    /// </summary>
    /// <param name="target">Целевой ассистент.</param>
    /// <param name="scope">Зона.</param>
    /// <param name="projectDir">Корень проекта.</param>
    /// <param name="force">Если файл существует — перезаписать; иначе вернуть <see cref="ErrorCode.InvalidArgs"/>.</param>
    /// <returns>Описание выполненной установки.</returns>
    /// <exception cref="TrackerException">При <see cref="ErrorCode.InvalidArgs"/> когда файл существует и <paramref name="force"/>=false.</exception>
    /// <exception cref="NotSupportedException">Если запрошена неподдерживаемая комбинация (например, Copilot+Global).</exception>
    public static InstallResult Install(SkillTarget target, SkillScope scope, string projectDir, bool force)
    {
        var path = SkillPaths.Resolve(target, scope, projectDir);
        var version = EmbeddedSkill.GetVersion();

        if (File.Exists(path) && !force)
        {
            throw new TrackerException(
                ErrorCode.InvalidArgs,
                $"file exists, use --force to overwrite: {path}");
        }
        EnsureParent(path);
        File.WriteAllText(path, BuildContent(target));
        TrySetPosixMode(path, 0b110_100_100); // 0644
        return new InstallResult(target, scope, path, version);
    }

    /// <summary>
    /// Пытается установить skill, но возвращает <c>null</c> с описанием в <paramref name="skipped"/>,
    /// если комбинация не поддерживается (Copilot+Global). Все остальные ошибки пробрасываются.
    /// </summary>
    /// <param name="target">Целевой ассистент.</param>
    /// <param name="scope">Зона.</param>
    /// <param name="projectDir">Корень проекта.</param>
    /// <param name="force">Перезаписать существующий файл.</param>
    /// <param name="skipped">Заполняется, если установка была пропущена из-за неподдерживаемой комбинации.</param>
    /// <returns>Результат установки или <c>null</c>, если она была пропущена.</returns>
    public static InstallResult? TryInstall(
        SkillTarget target,
        SkillScope scope,
        string projectDir,
        bool force,
        out SkippedInstall? skipped)
    {
        skipped = null;
        try
        {
            return Install(target, scope, projectDir, force);
        }
        catch (NotSupportedException ex)
        {
            skipped = new SkippedInstall(target, scope, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Удаляет skill из указанной локации. Возвращает <c>null</c> если файла не было.
    /// </summary>
    /// <param name="target">Целевой ассистент.</param>
    /// <param name="scope">Зона.</param>
    /// <param name="projectDir">Корень проекта.</param>
    /// <returns>Путь к удалённому файлу или <c>null</c>, если ничего удалять не было.</returns>
    /// <exception cref="NotSupportedException">Если запрошена неподдерживаемая комбинация (Copilot+Global).</exception>
    public static string? Uninstall(SkillTarget target, SkillScope scope, string projectDir)
    {
        var path = SkillPaths.Resolve(target, scope, projectDir);
        if (!File.Exists(path))
        {
            return null;
        }

        File.Delete(path);
        // Подчищаем пустой каталог .../skills/yt/ или .../rules/ (best-effort).
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
        {
            try { Directory.Delete(dir); } catch { /* best-effort */ }
        }
        return path;
    }

    /// <summary>
    /// Перезаписывает уже установленные локации актуальной версией. Локации,
    /// в которых skill не установлен, пропускаются.
    /// </summary>
    /// <param name="targets">Какие <see cref="SkillTarget"/> рассматривать.</param>
    /// <param name="scopes">Какие <see cref="SkillScope"/> рассматривать.</param>
    /// <param name="projectDir">Корень проекта.</param>
    /// <returns>Список переустановленных локаций (только тех, что были установлены).</returns>
    public static IReadOnlyList<InstallResult> Update(
        IReadOnlyCollection<SkillTarget> targets,
        IReadOnlyCollection<SkillScope> scopes,
        string projectDir)
    {
        var status = GetStatus(projectDir);
        var results = new List<InstallResult>();

        foreach (var inst in status.All())
        {
            if (!targets.Contains(inst.Target) || !scopes.Contains(inst.Scope))
            {
                continue;
            }

            // Force=true — апдейт всегда перезаписывает.
            var written = Install(inst.Target, inst.Scope, projectDir, force: true);
            results.Add(written with { FromVersion = inst.Version });
        }

        return results;
    }

    /// <summary>
    /// Возвращает контент, который будет записан в файл для конкретного target'а.
    /// Используется как <c>Install</c>, так и <c>show</c>-командой.
    /// </summary>
    /// <param name="target">Целевой ассистент.</param>
    /// <returns>Готовый к записи текст файла (с подставленной версией).</returns>
    public static string BuildContent(SkillTarget target)
    {
        var version = EmbeddedSkill.GetVersion();
        return target switch
        {
            // Claude / Codex / Gemini — все три едят полный SKILL.md as-is с YAML frontmatter.
            SkillTarget.Claude or SkillTarget.Codex or SkillTarget.Gemini => EmbeddedSkill.ReadAll(),
            SkillTarget.Cursor => BuildCursorMdc(version),
            SkillTarget.Copilot => BuildCopilotInstructions(version),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown skill target."),
        };
    }

    /// <summary>
    /// Универсальный probe: читает файл и извлекает версию из <c>yt-version</c>-маркера.
    /// </summary>
    private static Installation? Probe(SkillTarget target, SkillScope scope, string projectDir)
    {
        string path;
        try
        {
            path = SkillPaths.Resolve(target, scope, projectDir);
        }
        catch (NotSupportedException)
        {
            // Неподдерживаемая комбинация (например, Copilot+Global) — нечего пробить.
            return null;
        }

        if (!File.Exists(path))
        {
            return null;
        }
        var text = File.ReadAllText(path);
        var ver = EmbeddedSkill.TryExtractClaudeVersion(text) ?? "unknown";
        return new Installation(target, scope, path, ver);
    }

    private static void EnsureParent(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>
    /// Лучший-эффорт <c>chmod</c> на POSIX. На Windows — no-op.
    /// </summary>
    private static void TrySetPosixMode(string path, int mode)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }
        try
        {
            File.SetUnixFileMode(path, (UnixFileMode)mode);
        }
        catch
        {
            // best-effort; на некоторых FS chmod не поддерживается
        }
    }

    /// <summary>
    /// Собирает контент Cursor <c>.mdc</c> файла: Cursor-специфичный frontmatter +
    /// version-маркер + body после version-маркера в SKILL.md.
    /// </summary>
    private static string BuildCursorMdc(string version)
    {
        var description = EmbeddedSkill.GetDescription();
        var body = EmbeddedSkill.GetBodyAfterVersionMarker();
        return $"---\ndescription: {description}\nglobs:\nalwaysApply: false\n---\n\n<!-- yt-version: {version} -->\n\n{body}";
    }

    /// <summary>
    /// Собирает контент Copilot <c>.instructions.md</c> файла: <c>applyTo: "**"</c>-frontmatter +
    /// version-маркер + body после version-маркера в SKILL.md.
    /// </summary>
    private static string BuildCopilotInstructions(string version)
    {
        var description = EmbeddedSkill.GetDescription();
        var body = EmbeddedSkill.GetBodyAfterVersionMarker();
        return $"---\napplyTo: \"**\"\ndescription: {description}\n---\n\n<!-- yt-version: {version} -->\n\n{body}";
    }
}
