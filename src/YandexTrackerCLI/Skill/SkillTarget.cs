namespace YandexTrackerCLI.Skill;

/// <summary>
/// Цель установки skill'а — конкретный AI-ассистент.
/// </summary>
public enum SkillTarget
{
    /// <summary>Claude Code (skill в директории <c>~/.claude/skills/yt/SKILL.md</c>).</summary>
    Claude,

    /// <summary>OpenAI Codex (markdown-секция в <c>~/.codex/AGENTS.md</c>).</summary>
    Codex,
}

/// <summary>
/// Зона установки — глобальная (per-user) или per-project.
/// </summary>
public enum SkillScope
{
    /// <summary>Глобальная установка в HOME-директорию пользователя.</summary>
    Global,

    /// <summary>Установка в текущий или указанный через <c>--project-dir</c> проект.</summary>
    Project,
}
