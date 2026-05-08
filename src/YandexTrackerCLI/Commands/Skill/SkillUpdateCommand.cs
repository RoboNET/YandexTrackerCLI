namespace YandexTrackerCLI.Commands.Skill;

using System.CommandLine;
using Core.Api.Errors;
using Output;
using Spectre.Console;
using YandexTrackerCLI.Interactive;
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

                var format = CommandFormatHelper.ResolveForCommand(parseResult);
                var useProgress = format is not OutputFormat.Json and not OutputFormat.Minimal
                    && !Console.IsErrorRedirected
                    && !Console.IsOutputRedirected;

                IReadOnlyList<SkillManager.InstallResult> updated;
                if (useProgress)
                {
                    updated = RunWithSpectreProgress(targets, scopes, projectDir);
                }
                else
                {
                    updated = SkillManager.Update(targets, scopes, projectDir);
                }

                using var doc = SkillJsonFormatter.FormatUpdate(updated, hadAny);
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

    private static IReadOnlyList<SkillManager.InstallResult> RunWithSpectreProgress(
        IReadOnlyList<SkillTarget> targets,
        IReadOnlyList<SkillScope> scopes,
        string projectDir)
    {
        var ansi = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) });
        var results = new List<SkillManager.InstallResult>();
        var tasks = new Dictionary<(SkillTarget, SkillScope), ProgressTask>();

        ansi.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn(),
            })
            .Start(ctx =>
            {
                var progress = new SyncProgress<SkillProgressEvent>(evt =>
                {
                    var key = (evt.Target, evt.Scope);
                    switch (evt.Kind)
                    {
                        case SkillProgressKind.Started:
                            var task = ctx.AddTask(
                                $"{LabelFor(evt.Target, evt.Scope)} [grey]{Markup.Escape(evt.Path)}[/]",
                                maxValue: 100);
                            tasks[key] = task;
                            break;
                        case SkillProgressKind.Wrote:
                            if (tasks.TryGetValue(key, out var t1))
                            {
                                t1.Description = $"[green]✓[/] {LabelFor(evt.Target, evt.Scope)} [grey]→ {Markup.Escape(evt.Version ?? "?")}[/]";
                                t1.Increment(100);
                            }
                            break;
                        case SkillProgressKind.Skipped:
                            if (tasks.TryGetValue(key, out var t2))
                            {
                                t2.Description = $"[dim]⊘[/] {LabelFor(evt.Target, evt.Scope)} [dim](skipped)[/]";
                                t2.Increment(100);
                            }
                            break;
                        case SkillProgressKind.Failed:
                            if (tasks.TryGetValue(key, out var t3))
                            {
                                t3.Description = $"[red]✗[/] {LabelFor(evt.Target, evt.Scope)} [red]{Markup.Escape(evt.Error ?? "error")}[/]";
                                t3.Increment(100);
                            }
                            break;
                    }
                });
                var written = SkillManager.Update(targets, scopes, projectDir, progress);
                results.AddRange(written);
            });

        return results;
    }

    private static string LabelFor(SkillTarget t, SkillScope s) => $"{t} ({s})";
}
