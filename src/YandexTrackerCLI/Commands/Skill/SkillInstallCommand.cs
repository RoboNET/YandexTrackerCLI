namespace YandexTrackerCLI.Commands.Skill;

using System.CommandLine;
using Core.Api.Errors;
using Output;
using YandexTrackerCLI.Skill;

/// <summary>
/// Команда <c>yt skill install</c>: устанавливает skill в выбранные target × scope.
/// Для Copilot+Global возвращает <c>skipped</c>-запись (не error), т.к. Copilot
/// не поддерживает global-scope.
/// </summary>
public static class SkillInstallCommand
{
    /// <summary>
    /// Строит subcommand <c>install</c>.
    /// </summary>
    public static Command Build()
    {
        var targetOpt = SkillCommandOptions.Target();
        var scopeOpt = SkillCommandOptions.Scope();
        var projectDirOpt = SkillCommandOptions.ProjectDir();
        var forceOpt = new Option<bool>("--force")
        {
            Description = "Перезаписать существующий файл skill'а.",
        };

        var cmd = new Command("install", "Установить yt skill в Claude / Codex / Gemini / Cursor / Copilot.");
        cmd.Options.Add(targetOpt);
        cmd.Options.Add(scopeOpt);
        cmd.Options.Add(projectDirOpt);
        cmd.Options.Add(forceOpt);

        cmd.SetAction((parseResult, _) =>
        {
            try
            {
                var targets = SkillCommandOptions.ParseTargets(parseResult.GetValue(targetOpt) ?? SkillCommandOptions.TargetAll);
                var scopes = SkillCommandOptions.ParseScopes(parseResult.GetValue(scopeOpt) ?? SkillCommandOptions.ScopeGlobal);
                var projectDir = parseResult.GetValue(projectDirOpt) ?? Directory.GetCurrentDirectory();
                var force = parseResult.GetValue(forceOpt);

                var results = new List<SkillManager.InstallResult>();
                var skipped = new List<SkillManager.SkippedInstall>();
                foreach (var t in targets)
                {
                    foreach (var s in scopes)
                    {
                        var installed = SkillManager.TryInstall(t, s, projectDir, force, out var skip);
                        if (installed is not null)
                        {
                            results.Add(installed);
                        }
                        else if (skip is not null)
                        {
                            skipped.Add(skip);
                        }
                    }
                }

                using var doc = SkillJsonFormatter.FormatInstall(results, skipped);
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
