namespace YandexTrackerCLI.Skill;

using System.Runtime.InteropServices;
using Core.Api.Errors;

/// <summary>
/// Высокоуровневые операции над skill'ом: install/uninstall/status/update в
/// Claude Code и/или OpenAI Codex, глобально или per-project.
/// </summary>
/// <remarks>
/// Логика установки одинаковая для Claude и Codex — разница только в base path
/// (см. <see cref="SkillPaths"/>). Skill — это всегда один файл <c>SKILL.md</c>
/// с YAML frontmatter и version-маркером <c>&lt;!-- yt-version: X.Y.Z --&gt;</c>.
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
    /// Текущий статус установленных локаций.
    /// </summary>
    /// <param name="CurrentVersion">Версия бинаря.</param>
    /// <param name="ClaudeGlobal">Текущая Claude/global установка или <c>null</c>.</param>
    /// <param name="ClaudeProject">Текущая Claude/project установка или <c>null</c>.</param>
    /// <param name="CodexGlobal">Текущая Codex/global установка или <c>null</c>.</param>
    /// <param name="CodexProject">Текущая Codex/project установка или <c>null</c>.</param>
    public sealed record Status(
        string CurrentVersion,
        Installation? ClaudeGlobal,
        Installation? ClaudeProject,
        Installation? CodexGlobal,
        Installation? CodexProject)
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
    /// Возвращает текущий статус всех 4 возможных локаций (Claude/Codex × Global/Project).
    /// </summary>
    /// <param name="projectDir">Корень проекта (для project-scope).</param>
    /// <returns>Снимок состояния.</returns>
    public static Status GetStatus(string projectDir) =>
        new(
            EmbeddedSkill.GetVersion(),
            ClaudeGlobal: Probe(SkillTarget.Claude, SkillScope.Global, projectDir),
            ClaudeProject: Probe(SkillTarget.Claude, SkillScope.Project, projectDir),
            CodexGlobal: Probe(SkillTarget.Codex, SkillScope.Global, projectDir),
            CodexProject: Probe(SkillTarget.Codex, SkillScope.Project, projectDir));

    /// <summary>
    /// Устанавливает skill в указанную локацию. Поведение идентично для Claude и Codex —
    /// разница только в базовом каталоге.
    /// </summary>
    /// <param name="target">Целевой ассистент.</param>
    /// <param name="scope">Зона.</param>
    /// <param name="projectDir">Корень проекта.</param>
    /// <param name="force">Если файл существует — перезаписать; иначе вернуть <see cref="ErrorCode.InvalidArgs"/>.</param>
    /// <returns>Описание выполненной установки.</returns>
    /// <exception cref="TrackerException">При <see cref="ErrorCode.InvalidArgs"/> когда файл существует и <paramref name="force"/>=false.</exception>
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
        File.WriteAllText(path, EmbeddedSkill.ReadAll());
        TrySetPosixMode(path, 0b110_100_100); // 0644
        return new InstallResult(target, scope, path, version);
    }

    /// <summary>
    /// Удаляет skill из указанной локации. Возвращает <c>null</c> если файла не было.
    /// </summary>
    /// <param name="target">Целевой ассистент.</param>
    /// <param name="scope">Зона.</param>
    /// <param name="projectDir">Корень проекта.</param>
    /// <returns>Путь к удалённому файлу или <c>null</c>, если ничего удалять не было.</returns>
    public static string? Uninstall(SkillTarget target, SkillScope scope, string projectDir)
    {
        var path = SkillPaths.Resolve(target, scope, projectDir);
        if (!File.Exists(path))
        {
            return null;
        }

        File.Delete(path);
        // Подчищаем пустой каталог .../skills/yt/.
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
    /// Универсальный probe: читает файл и извлекает версию из <c>yt-version</c>-маркера.
    /// </summary>
    private static Installation? Probe(SkillTarget target, SkillScope scope, string projectDir)
    {
        var path = SkillPaths.Resolve(target, scope, projectDir);
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
}
