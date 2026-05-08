namespace YandexTrackerCLI.Commands.Checklist;

using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt checklist add-item &lt;issue-key&gt;</c>: добавляет пункт чек-листа
/// (<c>POST /v3/issues/{key}/checklistItems</c>). Тело собирается через
/// <see cref="JsonBodyReader.ReadAndMerge"/>: scalar inline-флаги
/// (<c>--text</c>, <c>--assignee</c>, <c>--deadline</c>) мерджатся поверх raw-payload.
/// Эффективное тело должно содержать поле <c>text</c>.
/// </summary>
public static class ChecklistAddItemCommand
{
    /// <summary>
    /// Строит subcommand <c>add-item</c> для <c>yt checklist</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("issue-key") { Description = "Ключ задачи (например DEV-1)." };
        var textOpt = new Option<string?>("--text") { Description = "Текст пункта чек-листа (override поля text)." };
        var assigneeOpt = new Option<string?>("--assignee") { Description = "Логин/ID исполнителя (override поля assignee)." };
        var deadlineOpt = new Option<string?>("--deadline")
        {
            Description = "Дедлайн ISO 8601 (override поля deadline; например 2024-01-15T10:00:00+03:00).",
        };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу с телом запроса." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

        var cmd = new Command(
            "add-item",
            "Добавить пункт чек-листа (POST /v3/issues/{key}/checklistItems).");
        cmd.Arguments.Add(keyArg);
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
                var text = pr.GetValue(textOpt);
                var assignee = pr.GetValue(assigneeOpt);
                var deadline = pr.GetValue(deadlineOpt);
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                if (!string.IsNullOrWhiteSpace(deadline))
                {
                    ValidateIso8601DateTime(deadline!);
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
                        "Provide --text (optionally --assignee/--deadline) or --json-file/--json-stdin.");

                using (var doc = JsonDocument.Parse(body))
                {
                    if (!doc.RootElement.TryGetProperty("text", out _))
                    {
                        throw new TrackerException(ErrorCode.InvalidArgs,
                            "Effective body must include 'text'.");
                    }
                }

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.PostJsonRawAsync(
                    $"issues/{Uri.EscapeDataString(key)}/checklistItems",
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

    /// <summary>
    /// Проверяет, что строка соответствует формату ISO 8601 date/time (парсится через
    /// <see cref="DateTimeOffset.TryParse(string, IFormatProvider, DateTimeStyles, out DateTimeOffset)"/>).
    /// Используется также из <see cref="ChecklistUpdateCommand"/>.
    /// </summary>
    /// <param name="value">Значение опции <c>--deadline</c>.</param>
    /// <exception cref="TrackerException">
    /// Бросается с <see cref="ErrorCode.InvalidArgs"/>, если строка не парсится.
    /// </exception>
    internal static void ValidateIso8601DateTime(string value)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out _))
        {
            throw new TrackerException(
                ErrorCode.InvalidArgs,
                $"--deadline must be an ISO 8601 date/time (e.g. 2024-01-15T10:00:00+03:00), got '{value}'.");
        }
    }
}
