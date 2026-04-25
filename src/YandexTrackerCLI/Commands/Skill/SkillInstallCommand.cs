namespace YandexTrackerCLI.Commands.Skill;

using System.CommandLine;
using Core.Api.Errors;
using Output;
using YandexTrackerCLI.Skill;

/// <summary>
/// Команда <c>yt skill install</c>: устанавливает skill в выбранные target × scope.
/// Поддерживает интерактивный prompt в TTY, если <c>--target</c>/<c>--scope</c>/<c>--no-prompt</c>
/// не переданы. Для Copilot+Global возвращает <c>skipped</c>-запись (не error), т.к. Copilot
/// не поддерживает global-scope.
/// </summary>
public static class SkillInstallCommand
{
    /// <summary>
    /// Строит subcommand <c>install</c>.
    /// </summary>
    public static Command Build()
    {
        // Используем НЕ-defaulted nullable-варианты, чтобы отличить «пользователь явно
        // передал значение» от «значение по дефолту». В non-interactive (или при
        // --no-prompt) null резолвится в old defaults: target=all, scope=global.
        var targetOpt = new Option<string?>("--target")
        {
            Description = "claude | codex | gemini | cursor | copilot | all (default all).",
        };
        var scopeOpt = new Option<string?>("--scope")
        {
            Description = "global | project (default global).",
        };
        var projectDirOpt = SkillCommandOptions.ProjectDir();
        var forceOpt = new Option<bool>("--force")
        {
            Description = "Перезаписать существующий файл skill'а.",
        };
        var noPromptOpt = new Option<bool>("--no-prompt")
        {
            Description = "Не запускать интерактивный prompt — использовать default'ы (target=all, scope=global). Для CI/скриптов.",
        };

        var cmd = new Command("install", "Установить yt skill в Claude / Codex / Gemini / Cursor / Copilot.");
        cmd.Options.Add(targetOpt);
        cmd.Options.Add(scopeOpt);
        cmd.Options.Add(projectDirOpt);
        cmd.Options.Add(forceOpt);
        cmd.Options.Add(noPromptOpt);

        cmd.SetAction((parseResult, _) =>
        {
            try
            {
                var rawTarget = parseResult.GetValue(targetOpt);
                var rawScope = parseResult.GetValue(scopeOpt);
                var projectDir = parseResult.GetValue(projectDirOpt) ?? Directory.GetCurrentDirectory();
                var force = parseResult.GetValue(forceOpt);
                var noPrompt = parseResult.GetValue(noPromptOpt);

                var userPassedTarget = rawTarget is not null;
                var userPassedScope = rawScope is not null;

                IReadOnlyList<SkillTarget> targets;
                IReadOnlyList<SkillScope> scopes;

                if (SkillInstallCommandHelpers.ShouldRunInteractive(userPassedTarget, userPassedScope, noPrompt))
                {
                    var prompt = SkillInstallPrompt.Current;
                    var allTargets = SkillCommandOptions.AllTargets;
                    var detected = SkillInstallDetector.DetectInstalled(includeCopilot: false);

                    var chosen = prompt.PromptTargets(allTargets, detected);
                    if (chosen.Length == 0)
                    {
                        // Пользователь выбрал "0" — ничего не ставим.
                        using var emptyDoc = SkillJsonFormatter.FormatInstall(
                            Array.Empty<SkillManager.InstallResult>(),
                            Array.Empty<SkillManager.SkippedInstall>());
                        var fmtEmpty = CommandFormatHelper.ResolveForCommand(parseResult);
                        JsonWriter.Write(Console.Out, emptyDoc.RootElement, fmtEmpty, pretty: CommandFormatHelper.ResolvePretty());
                        return Task.FromResult(0);
                    }
                    targets = chosen;

                    var chosenScope = prompt.PromptScope();
                    scopes = new[] { chosenScope };

                    if (!force)
                    {
                        var existing = SkillInstallCommandHelpers.CollectExistingPaths(targets, chosenScope, projectDir);
                        if (existing.Count > 0)
                        {
                            force = prompt.PromptOverwrite(existing);
                        }
                    }
                }
                else
                {
                    targets = SkillCommandOptions.ParseTargets(rawTarget ?? SkillCommandOptions.TargetAll);
                    scopes = SkillCommandOptions.ParseScopes(rawScope ?? SkillCommandOptions.ScopeGlobal);
                }

                var results = new List<SkillManager.InstallResult>();
                var skipped = new List<SkillManager.SkippedInstall>();
                foreach (var t in targets)
                {
                    foreach (var s in scopes)
                    {
                        try
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
                        catch (TrackerException ex) when (ex.Code == ErrorCode.InvalidArgs)
                        {
                            // Файл существует и пользователь отказался от --force.
                            // В non-interactive (CLI без --force) это ошибка; чтобы сохранить
                            // совместимость со старым поведением, пробрасываем дальше.
                            // В interactive-режиме пользователь уже видел prompt, поэтому
                            // тихо пропускаем (записываем как skipped).
                            if (SkillInstallCommandHelpers.ShouldRunInteractive(userPassedTarget, userPassedScope, noPrompt))
                            {
                                skipped.Add(new SkillManager.SkippedInstall(t, s, ex.Message));
                            }
                            else
                            {
                                throw;
                            }
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
