namespace YandexTrackerCLI.Commands.Checklist;

using System.CommandLine;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt checklist update &lt;issue-key&gt; &lt;item-id&gt;</c>: обновляет пункт
/// чек-листа (<c>PATCH /v3/issues/{key}/checklistItems/{itemId}</c>). Тело собирается
/// через <see cref="JsonBodyReader.ReadAndMerge"/>: scalar inline-флаги
/// (<c>--text</c>, <c>--assignee</c>, <c>--deadline</c>) мерджатся поверх raw-payload.
/// </summary>
public static class ChecklistUpdateCommand
{
    /// <summary>
    /// Строит subcommand <c>update</c> для <c>yt checklist</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("issue-key") { Description = "Ключ задачи (например DEV-1)." };
        var itemIdArg = new Argument<string>("item-id") { Description = "Идентификатор пункта чек-листа." };
        var textOpt = new Option<string?>("--text") { Description = "Новый текст пункта (override поля text)." };
        var assigneeOpt = new Option<string?>("--assignee") { Description = "Новый исполнитель (override поля assignee)." };
        var deadlineOpt = new Option<string?>("--deadline") { Description = "Новый дедлайн ISO 8601 (override поля deadline)." };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу с телом запроса." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

        var cmd = new Command(
            "update",
            "Обновить пункт чек-листа (PATCH /v3/issues/{key}/checklistItems/{itemId}).");
        cmd.Arguments.Add(keyArg);
        cmd.Arguments.Add(itemIdArg);
        cmd.Options.Add(textOpt);
        cmd.Options.Add(assigneeOpt);
        cmd.Options.Add(deadlineOpt);
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                var itemId = pr.GetValue(itemIdArg)!;
                var text = pr.GetValue(textOpt);
                var assignee = pr.GetValue(assigneeOpt);
                var deadline = pr.GetValue(deadlineOpt);
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                if (!string.IsNullOrWhiteSpace(deadline))
                {
                    ChecklistAddItemCommand.ValidateIso8601DateTime(deadline!);
                }

                var overrides = new List<(string, JsonBodyMerger.OverrideValue)>();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    overrides.Add(("text", JsonBodyMerger.OverrideValue.Of(text!)));
                }
                if (!string.IsNullOrWhiteSpace(assignee))
                {
                    overrides.Add(("assignee", JsonBodyMerger.OverrideValue.Of(assignee!)));
                }
                if (!string.IsNullOrWhiteSpace(deadline))
                {
                    overrides.Add(("deadline", JsonBodyMerger.OverrideValue.Of(deadline!)));
                }

                var body = JsonBodyReader.ReadAndMerge(jsonFile, jsonStdin, Console.In, overrides)
                    ?? throw new TrackerException(ErrorCode.InvalidArgs,
                        "Provide at least one of --text/--assignee/--deadline or --json-file/--json-stdin.");

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.PatchJsonAsync(
                    $"issues/{Uri.EscapeDataString(key)}/checklistItems/{Uri.EscapeDataString(itemId)}",
                    body,
                    ct);
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
