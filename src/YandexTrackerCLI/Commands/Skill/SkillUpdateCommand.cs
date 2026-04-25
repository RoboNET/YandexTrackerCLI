namespace YandexTrackerCLI.Commands.Skill;

using System.CommandLine;
using Core.Api.Errors;
using Output;
using YandexTrackerCLI.Skill;

/// <summary>
/// Команда <c>yt skill update</c>: переустанавливает уже установленные локации актуальной версией.
/// </summary>
public static class SkillUpdateCommand
{
    /// <summary>
    /// Строит subcommand <c>update</c>.
    /// </summary>
    public static Command Build()
    {
        var targetOpt = SkillCommandOptions.Target(
            defaultValue: SkillCommandOptions.TargetAll,
            description: "claude | codex | gemini | cursor | copilot | all — какие target'ы рассматривать (default all).");
        var scopeOpt = SkillCommandOptions.Scope(
            defaultValue: SkillCommandOptions.TargetAll,
            description: "global | project | all — какие зоны рассматривать (default all).");
        var projectDirOpt = SkillCommandOptions.ProjectDir();

        var cmd = new Command("update", "Обновить уже установленные skill-локации до текущей версии CLI.");
        cmd.Options.Add(targetOpt);
        cmd.Options.Add(scopeOpt);
        cmd.Options.Add(projectDirOpt);

        cmd.SetAction((parseResult, _) =>
        {
            try
            {
                var targets = SkillCommandOptions.ParseTargets(parseResult.GetValue(targetOpt) ?? SkillCommandOptions.TargetAll);
                var scopes = SkillCommandOptions.ParseScopes(parseResult.GetValue(scopeOpt) ?? SkillCommandOptions.TargetAll);
                var projectDir = parseResult.GetValue(projectDirOpt) ?? Directory.GetCurrentDirectory();

                var status = SkillManager.GetStatus(projectDir);
                var hadAny = status.All().Any();

                var updated = SkillManager.Update(targets, scopes, projectDir);

                using var doc = SkillJsonFormatter.FormatUpdate(updated, hadAny);
                var format = CommandFormatHelper.ResolveForCommand(parseResult);
                JsonWriter.Write(Console.Out, doc.RootElement, format, pretty: CommandFormatHelper.ResolvePretty());
                return Task.FromResult(0);
            }
            catch (TrackerException ex)
            {
                ErrorWriter.Write(Console.Error, ex);
                return Task.FromResult(ex.Code.ToExitCode());
            }
        });
        return cmd;
    }
}
