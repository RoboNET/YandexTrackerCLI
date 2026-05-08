namespace YandexTrackerCLI.Commands.Issue;

using System.CommandLine;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt issue update &lt;key&gt;</c>: обновляет задачу
/// (<c>PATCH /v3/issues/{key}</c>). Тело собирается через
/// <see cref="JsonBodyReader.ReadAndMerge"/>: scalar inline-флаги
/// (<c>--summary</c>, <c>--description</c>, <c>--type</c>, <c>--priority</c>,
/// <c>--assignee</c>) мерджатся поверх raw-payload.
/// </summary>
public static class IssueUpdateCommand
{
    /// <summary>
    /// Строит subcommand <c>update</c> для <c>yt issue</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("key") { Description = "Ключ задачи (например DEV-1)." };

        var summaryOpt = new Option<string?>("--summary") { Description = "Новый заголовок (override поля summary)." };
        var descriptionOpt = new Option<string?>("--description") { Description = "Новое описание (override поля description)." };
        var typeOpt = new Option<string?>("--type") { Description = "Новый тип (override поля type)." };
        var priorityOpt = new Option<string?>("--priority") { Description = "Новый приоритет (override поля priority)." };
        var assigneeOpt = new Option<string?>("--assignee") { Description = "Новый исполнитель (override поля assignee)." };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу с телом запроса." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

        var cmd = new Command("update", "Обновить задачу (PATCH /v3/issues/{key}).");
        cmd.Arguments.Add(keyArg);
        cmd.Options.Add(summaryOpt);
        cmd.Options.Add(descriptionOpt);
        cmd.Options.Add(typeOpt);
        cmd.Options.Add(priorityOpt);
        cmd.Options.Add(assigneeOpt);
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                var summary = pr.GetValue(summaryOpt);
                var description = pr.GetValue(descriptionOpt);
                var type = pr.GetValue(typeOpt);
                var priority = pr.GetValue(priorityOpt);
                var assignee = pr.GetValue(assigneeOpt);
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                var overrides = new List<(string, JsonBodyMerger.OverrideValue)>();
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    overrides.Add(("summary", JsonBodyMerger.OverrideValue.Of(summary!)));
                }
                if (!string.IsNullOrWhiteSpace(description))
                {
                    overrides.Add(("description", JsonBodyMerger.OverrideValue.Of(description!)));
                }
                if (!string.IsNullOrWhiteSpace(type))
                {
                    overrides.Add(("type", JsonBodyMerger.OverrideValue.Of(type!)));
                }
                if (!string.IsNullOrWhiteSpace(priority))
                {
                    overrides.Add(("priority", JsonBodyMerger.OverrideValue.Of(priority!)));
                }
                if (!string.IsNullOrWhiteSpace(assignee))
                {
                    overrides.Add(("assignee", JsonBodyMerger.OverrideValue.Of(assignee!)));
                }

                var body = JsonBodyReader.ReadAndMerge(jsonFile, jsonStdin, Console.In, overrides)
                    ?? throw new TrackerException(ErrorCode.InvalidArgs,
                        "Nothing to update: specify at least one typed option or use --json-file/--json-stdin.");

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.PatchJsonAsync($"issues/{Uri.EscapeDataString(key)}", body, ct);
                JsonWriter.Write(Console.Out, result, ctx.EffectiveOutputFormat, pretty: !Console.IsOutputRedirected);
                return 0;
            }
            catch (TrackerException ex)
            {
                ErrorWriter.Write(Console.Error, ex);
                return ex.Code.ToExitCode();
            }
        });

        return cmd;
    }
}
