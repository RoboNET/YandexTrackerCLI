namespace YandexTrackerCLI.Commands.Skill;

using System.CommandLine;
using YandexTrackerCLI.Skill;

/// <summary>
/// Общие опции группы команд <c>yt skill</c>: выбор target/scope/project-dir.
/// </summary>
internal static class SkillCommandOptions
{
    public const string TargetClaude = "claude";
    public const string TargetCodex = "codex";
    public const string TargetAll = "all";

    public const string ScopeGlobal = "global";
    public const string ScopeProject = "project";

    public static Option<string> Target(string defaultValue = TargetAll, string description = "claude | codex | all (default all).") =>
        new("--target")
        {
            Description = description,
            DefaultValueFactory = _ => defaultValue,
        };

    public static Option<string> Scope(string defaultValue = ScopeGlobal, string description = "global | project (default global).") =>
        new("--scope")
        {
            Description = description,
            DefaultValueFactory = _ => defaultValue,
        };

    public static Option<string?> ProjectDir() =>
        new("--project-dir")
        {
            Description = "Корень проекта (default cwd). Используется только при --scope project.",
        };

    /// <summary>
    /// Парсит строковое значение <c>--target</c> в массив enum'ов.
    /// </summary>
    public static SkillTarget[] ParseTargets(string raw) => raw.ToLowerInvariant() switch
    {
        TargetClaude => new[] { SkillTarget.Claude },
        TargetCodex => new[] { SkillTarget.Codex },
        TargetAll => new[] { SkillTarget.Claude, SkillTarget.Codex },
        _ => throw new InvalidOperationException($"Unknown --target value: {raw}"),
    };

    /// <summary>
    /// Парсит строковое значение <c>--scope</c> в массив enum'ов. Значение <c>all</c>
    /// поддержано как alias <c>global+project</c> (для <c>update</c>).
    /// </summary>
    public static SkillScope[] ParseScopes(string raw) => raw.ToLowerInvariant() switch
    {
        ScopeGlobal => new[] { SkillScope.Global },
        ScopeProject => new[] { SkillScope.Project },
        TargetAll => new[] { SkillScope.Global, SkillScope.Project },
        _ => throw new InvalidOperationException($"Unknown --scope value: {raw}"),
    };
}
