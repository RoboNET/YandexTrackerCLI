namespace YandexTrackerCLI.Commands.Skill;

using System.CommandLine;
using Core.Api.Errors;
using Output;
using YandexTrackerCLI.Skill;

/// <summary>
/// Команда <c>yt skill show</c>: печатает содержимое skill'а как оно бы было записано.
/// Поскольку Claude и Codex используют один и тот же файл SKILL.md, опция <c>--target</c>
/// сохранена для совместимости и для будущих расширений.
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
            description: "claude | codex (одинаковое содержимое — оставлено для совместимости).");

        var cmd = new Command("show", "Напечатать содержимое skill'а как оно было бы установлено.");
        cmd.Options.Add(targetOpt);

        cmd.SetAction((parseResult, _) =>
        {
            try
            {
                var target = (parseResult.GetValue(targetOpt) ?? SkillCommandOptions.TargetClaude).ToLowerInvariant();
                if (target == SkillCommandOptions.TargetAll)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "yt skill show requires a single target: claude or codex.");
                }
                // Validate the value parses; we don't use the parsed enum directly
                // because SKILL.md is identical for Claude and Codex.
                SkillCommandOptions.ParseTargets(target);

                var content = EmbeddedSkill.ReadAll();
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
