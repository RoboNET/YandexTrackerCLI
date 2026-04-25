namespace YandexTrackerCLI.Commands.Skill;

using System.CommandLine;
using Core.Api.Errors;
using Output;
using YandexTrackerCLI.Skill;

/// <summary>
/// Команда <c>yt skill status</c>: показывает что и где установлено + версии + флаг up_to_date.
/// </summary>
public static class SkillStatusCommand
{
    /// <summary>
    /// Строит subcommand <c>status</c>.
    /// </summary>
    public static Command Build()
    {
        var projectDirOpt = SkillCommandOptions.ProjectDir();
        var cmd = new Command("status", "Показать состояние установленных skill-локаций.");
        cmd.Options.Add(projectDirOpt);

        cmd.SetAction((parseResult, _) =>
        {
            try
            {
                var projectDir = parseResult.GetValue(projectDirOpt) ?? Directory.GetCurrentDirectory();
                var status = SkillManager.GetStatus(projectDir);
                var state = SafeLoadState();

                using var doc = SkillJsonFormatter.FormatStatus(status, state);
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

    private static SkillPromptState SafeLoadState()
    {
        try { return SkillPromptState.Load(); }
        catch { return new SkillPromptState(); }
    }
}
