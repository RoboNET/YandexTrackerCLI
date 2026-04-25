namespace YandexTrackerCLI.Skill;

/// <summary>
/// Цель установки skill'а — конкретный AI-ассистент.
/// </summary>
public enum SkillTarget
{
    /// <summary>Claude Code (skill в директории <c>~/.claude/skills/yt/SKILL.md</c>).</summary>
    Claude,

    /// <summary>OpenAI Codex (skill в <c>~/.agents/skills/yt/SKILL.md</c>).</summary>
    Codex,

    /// <summary>Gemini CLI (skill в <c>~/.gemini/skills/yt/SKILL.md</c>).</summary>
    Gemini,

    /// <summary>Cursor IDE (rule в <c>~/.cursor/rules/yt.mdc</c>).</summary>
    Cursor,

    /// <summary>GitHub Copilot (instructions в <c>&lt;projectDir&gt;/.github/instructions/yt.instructions.md</c>).
    /// Поддерживает только project-scope.</summary>
    Copilot,
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
