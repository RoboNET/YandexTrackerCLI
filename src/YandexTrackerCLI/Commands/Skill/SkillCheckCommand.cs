namespace YandexTrackerCLI.Commands.Skill;

using System.CommandLine;
using Core.Api.Errors;
using Output;
using YandexTrackerCLI.Skill;

/// <summary>
/// Команда <c>yt skill check</c>: вручную запускает проверку устаревших skill-локаций.
/// Поддерживает <c>--no-prompt</c> (только статус) и <c>--reset-prompt-state</c>.
/// </summary>
public static class SkillCheckCommand
{
    /// <summary>
    /// Строит subcommand <c>check</c>.
    /// </summary>
    public static Command Build()
    {
        var projectDirOpt = SkillCommandOptions.ProjectDir();
        var noPromptOpt = new Option<bool>("--no-prompt")
        {
            Description = "Не задавать интерактивный вопрос — только напечатать JSON со статусом.",
        };
        var resetOpt = new Option<bool>("--reset-prompt-state")
        {
            Description = "Сбросить state-файл (declined-версии, never_prompt) и выйти.",
        };

        var cmd = new Command("check", "Проверить актуальность установленных skill-локаций.");
        cmd.Options.Add(projectDirOpt);
        cmd.Options.Add(noPromptOpt);
        cmd.Options.Add(resetOpt);

        cmd.SetAction((parseResult, _) =>
        {
            try
            {
                if (parseResult.GetValue(resetOpt))
                {
                    var deleted = SkillPromptState.Reset();
                    CommandOutput.WriteSingleField("reset", deleted ? "ok" : "noop");
                    return Task.FromResult(0);
                }

                var projectDir = parseResult.GetValue(projectDirOpt) ?? Directory.GetCurrentDirectory();
                var noPrompt = parseResult.GetValue(noPromptOpt);

                if (noPrompt)
                {
                    var status = SkillManager.GetStatus(projectDir);
                    var state = SafeLoadState();
                    using var doc = SkillJsonFormatter.FormatStatus(status, state);
                    var format = CommandFormatHelper.ResolveForCommand(parseResult);
                    JsonWriter.Write(Console.Out, doc.RootElement, format, pretty: CommandFormatHelper.ResolvePretty());
                    return Task.FromResult(0);
                }

                // Запускаем общий механизм auto-check (TTY → prompt; pipe → warning).
                SkillAutoCheck.RunIfNeeded(projectDir);

                // После проверки печатаем итоговый статус.
                var afterStatus = SkillManager.GetStatus(projectDir);
                var afterState = SafeLoadState();
                using var afterDoc = SkillJsonFormatter.FormatStatus(afterStatus, afterState);
                var afterFormat = CommandFormatHelper.ResolveForCommand(parseResult);
                JsonWriter.Write(Console.Out, afterDoc.RootElement, afterFormat, pretty: CommandFormatHelper.ResolvePretty());
                return Task.FromResult(0);
            }
            catch (TrackerException ex)
            {
                ErrorWriter.Write(Console.Error, ex);
                return Task.FromResult(ex.Code.ToExitCode());
            }
        });
        return cmd;

        static SkillPromptState SafeLoadState()
        {
            try { return SkillPromptState.Load(); }
            catch { return new SkillPromptState(); }
        }
    }
}
