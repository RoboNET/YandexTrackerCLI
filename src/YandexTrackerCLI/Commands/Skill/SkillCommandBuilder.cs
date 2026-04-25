namespace YandexTrackerCLI.Commands.Skill;

using System.CommandLine;

/// <summary>
/// Контейнер группы <c>yt skill</c>: install / uninstall / status / show / update / check.
/// </summary>
public static class SkillCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>skill</c>.
    /// </summary>
    public static Command Build()
    {
        var cmd = new Command("skill", "Управление AI-skill (Claude Code, OpenAI Codex).");
        cmd.Subcommands.Add(SkillInstallCommand.Build());
        cmd.Subcommands.Add(SkillUninstallCommand.Build());
        cmd.Subcommands.Add(SkillStatusCommand.Build());
        cmd.Subcommands.Add(SkillShowCommand.Build());
        cmd.Subcommands.Add(SkillUpdateCommand.Build());
        cmd.Subcommands.Add(SkillCheckCommand.Build());
        return cmd;
    }
}
