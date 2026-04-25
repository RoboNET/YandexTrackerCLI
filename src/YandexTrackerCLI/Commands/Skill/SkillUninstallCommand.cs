namespace YandexTrackerCLI.Commands.Skill;

using System.CommandLine;
using Core.Api.Errors;
using Output;
using YandexTrackerCLI.Skill;

/// <summary>
/// Команда <c>yt skill uninstall</c>: удаляет установленные локации skill'а.
/// </summary>
public static class SkillUninstallCommand
{
    /// <summary>
    /// Строит subcommand <c>uninstall</c>.
    /// </summary>
    public static Command Build()
    {
        var targetOpt = SkillCommandOptions.Target();
        var scopeOpt = SkillCommandOptions.Scope();
        var projectDirOpt = SkillCommandOptions.ProjectDir();

        var cmd = new Command("uninstall", "Удалить yt skill из Claude / Codex / Gemini / Cursor / Copilot.");
        cmd.Options.Add(targetOpt);
        cmd.Options.Add(scopeOpt);
        cmd.Options.Add(projectDirOpt);

        cmd.SetAction((parseResult, _) =>
        {
            try
            {
                var targets = SkillCommandOptions.ParseTargets(parseResult.GetValue(targetOpt) ?? SkillCommandOptions.TargetAll);
                var scopes = SkillCommandOptions.ParseScopes(parseResult.GetValue(scopeOpt) ?? SkillCommandOptions.ScopeGlobal);
                var projectDir = parseResult.GetValue(projectDirOpt) ?? Directory.GetCurrentDirectory();

                var uninstalled = new List<(SkillTarget, SkillScope, string)>();
                var skipped = new List<(SkillTarget, SkillScope, string)>();
                foreach (var t in targets)
                {
                    foreach (var s in scopes)
                    {
                        // Copilot+Global — недоступная комбинация; молча пропускаем без записи
                        // в "skipped" (в skipped попадают только установленные-но-не-найденные).
                        string path;
                        try
                        {
                            path = SkillPaths.Resolve(t, s, projectDir);
                        }
                        catch (NotSupportedException)
                        {
                            continue;
                        }

                        if (File.Exists(path))
                        {
                            var deleted = SkillManager.Uninstall(t, s, projectDir);
                            if (deleted is not null)
                            {
                                uninstalled.Add((t, s, deleted));
                            }
                        }
                        else
                        {
                            skipped.Add((t, s, path));
                        }
                    }
                }

                using var doc = SkillJsonFormatter.FormatUninstall(uninstalled, skipped);
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
