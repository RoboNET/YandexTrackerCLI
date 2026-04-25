namespace YandexTrackerCLI.Commands.Checklist;

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt checklist update &lt;issue-key&gt; &lt;item-id&gt;</c>: обновляет пункт
/// чек-листа (<c>PATCH /v3/issues/{key}/checklistItems/{itemId}</c>). Поддерживает два
/// режима:
/// <list type="bullet">
///   <item><description>
///     <b>Typed</b> — через <c>--text</c>, <c>--assignee</c>, <c>--deadline</c>
///     (все опциональны, но требуется хотя бы одно).
///   </description></item>
///   <item><description>
///     <b>Raw JSON</b> — через <c>--json-file &lt;path&gt;</c> или <c>--json-stdin</c>
///     (взаимоисключается с typed-опциями).
///   </description></item>
/// </list>
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
        var textOpt = new Option<string?>("--text") { Description = "Новый текст пункта." };
        var assigneeOpt = new Option<string?>("--assignee") { Description = "Новый исполнитель пункта." };
        var deadlineOpt = new Option<string?>("--deadline")
        {
            Description = "Новый дедлайн в формате ISO 8601.",
        };
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
                        "Provide at least one of --text/--assignee/--deadline or --json-file/--json-stdin.");
                }

                string body;
                if (hasRaw)
                {
                    body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                        ?? throw new TrackerException(ErrorCode.InvalidArgs, "Empty request body.");
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(deadline))
                    {
                        ChecklistAddItemCommand.ValidateIso8601DateTime(deadline);
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

    /// <summary>
    /// Строит JSON-тело запроса для типизированного update-режима, включая только заданные
    /// (non-null, non-whitespace) поля.
    /// </summary>
    /// <param name="text">Опциональный новый текст.</param>
    /// <param name="assignee">Опциональный новый исполнитель.</param>
    /// <param name="deadline">Опциональный новый ISO 8601 дедлайн.</param>
    /// <returns>Сериализованный компактный JSON-объект.</returns>
    private static string BuildTypedBody(string? text, string? assignee, string? deadline)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            if (!string.IsNullOrWhiteSpace(text))
            {
                w.WriteString("text", text);
            }
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
}
