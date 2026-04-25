namespace YandexTrackerCLI.Commands.Checklist;

using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt checklist add-item &lt;issue-key&gt;</c>: добавляет пункт чек-листа
/// задачи (<c>POST /v3/issues/{key}/checklistItems</c>). Поддерживает два режима:
/// <list type="bullet">
///   <item><description>
///     <b>Typed</b> — через <c>--text</c> (обязательный), плюс опционально
///     <c>--assignee</c> и <c>--deadline &lt;ISO8601&gt;</c>. В теле запроса формируется
///     объект <c>{"text":"...","assignee":"...","deadline":"..."}</c>.
///   </description></item>
///   <item><description>
///     <b>Raw JSON</b> — через <c>--json-file &lt;path&gt;</c> или <c>--json-stdin</c>
///     (взаимоисключается с typed-опциями).
///   </description></item>
/// </list>
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
        var textOpt = new Option<string?>("--text") { Description = "Текст пункта чек-листа." };
        var assigneeOpt = new Option<string?>("--assignee") { Description = "Логин или ID исполнителя пункта." };
        var deadlineOpt = new Option<string?>("--deadline")
        {
            Description = "Дедлайн пункта в формате ISO 8601 (например 2024-01-15T10:00:00+03:00).",
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

                var hasTyped = !string.IsNullOrWhiteSpace(text)
                    || !string.IsNullOrWhiteSpace(assignee)
                    || !string.IsNullOrWhiteSpace(deadline);
                var hasRaw = !string.IsNullOrWhiteSpace(jsonFile) || jsonStdin;

                if (hasTyped && hasRaw)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "Cannot combine --text/--assignee/--deadline with --json-file/--json-stdin.");
                }

                if (!hasTyped && !hasRaw)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "Provide --text (optionally --assignee/--deadline) or --json-file/--json-stdin.");
                }

                string body;
                if (hasRaw)
                {
                    body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                        ?? throw new TrackerException(ErrorCode.InvalidArgs, "Empty request body.");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        throw new TrackerException(
                            ErrorCode.InvalidArgs,
                            "--text is required in typed mode.");
                    }

                    if (!string.IsNullOrWhiteSpace(deadline))
                    {
                        ValidateIso8601DateTime(deadline);
                    }

                    body = BuildTypedBody(text, assignee, deadline);
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
    /// Строит JSON-тело запроса для типизированного режима, включая только заданные
    /// (non-null, non-whitespace) поля.
    /// </summary>
    /// <param name="text">Текст пункта (обязательно).</param>
    /// <param name="assignee">Опциональный исполнитель.</param>
    /// <param name="deadline">Опциональный ISO 8601 дедлайн.</param>
    /// <returns>Сериализованный компактный JSON-объект.</returns>
    internal static string BuildTypedBody(string text, string? assignee, string? deadline)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("text", text);
            if (!string.IsNullOrWhiteSpace(assignee))
            {
                w.WriteString("assignee", assignee);
            }
            if (!string.IsNullOrWhiteSpace(deadline))
            {
                w.WriteString("deadline", deadline);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Проверяет, что строка соответствует формату ISO 8601 date/time (парсится через
    /// <see cref="DateTimeOffset.TryParse(string, IFormatProvider, DateTimeStyles, out DateTimeOffset)"/>).
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
