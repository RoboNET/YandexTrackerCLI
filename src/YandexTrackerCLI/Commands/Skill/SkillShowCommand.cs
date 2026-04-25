namespace YandexTrackerCLI.Commands.Skill;

using System.CommandLine;
using Core.Api.Errors;
using Output;
using YandexTrackerCLI.Skill;

/// <summary>
/// Команда <c>yt skill show</c>: печатает содержимое skill'а как оно бы было записано
/// для конкретного target'а. Claude/Codex/Gemini используют один и тот же файл SKILL.md;
/// Cursor печатает <c>.mdc</c> с переписанным frontmatter; Copilot — <c>.instructions.md</c>
/// с <c>applyTo</c>.
/// </summary>
public static class SkillShowCommand
{
    /// <summary>
    /// Строит subcommand <c>show</c>.
    /// </summary>
    public static Command Build()
    {
        var targetOpt = SkillCommandOptions.Target(
            defaultValue: SkillCommandOptions.TargetClaude,
            description: "claude | codex | gemini | cursor | copilot — формат файла, который будет напечатан.");

        var cmd = new Command("show", "Напечатать содержимое skill'а как оно было бы установлено.");
        cmd.Options.Add(targetOpt);

        cmd.SetAction((parseResult, _) =>
        {
            try
            {
                var raw = (parseResult.GetValue(targetOpt) ?? SkillCommandOptions.TargetClaude).ToLowerInvariant();
                if (raw == SkillCommandOptions.TargetAll)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "yt skill show requires a single target: claude | codex | gemini | cursor | copilot.");
                }

                var targets = SkillCommandOptions.ParseTargets(raw);
                var content = SkillManager.BuildContent(targets[0]);
                Console.Out.Write(content);
                if (!content.EndsWith('\n'))
                {
                    Console.Out.WriteLine();
                }
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
