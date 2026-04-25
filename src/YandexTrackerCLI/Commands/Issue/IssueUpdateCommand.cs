namespace YandexTrackerCLI.Commands.Issue;

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt issue update &lt;key&gt;</c>: обновляет задачу (<c>PATCH /v3/issues/{key}</c>).
/// Поддерживает два режима задания payload:
/// <list type="bullet">
///   <item><description>
///     <b>Typed</b> — через опциональные флаги <c>--summary</c>, <c>--description</c>,
///     <c>--type</c>, <c>--priority</c>, <c>--assignee</c>. Должен быть указан хотя бы один
///     (иначе нечего обновлять).
///   </description></item>
///   <item><description>
///     <b>Raw JSON</b> — через <c>--json-file &lt;path&gt;</c> или <c>--json-stdin</c>
///     (взаимоисключается с typed-флагами).
///   </description></item>
/// </list>
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

        var summaryOpt = new Option<string?>("--summary") { Description = "Новый заголовок задачи." };
        var descriptionOpt = new Option<string?>("--description") { Description = "Новое описание." };
        var typeOpt = new Option<string?>("--type") { Description = "Новый тип задачи (bug, task, ...)." };
        var priorityOpt = new Option<string?>("--priority") { Description = "Новый приоритет." };
        var assigneeOpt = new Option<string?>("--assignee") { Description = "Новый исполнитель." };
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

                var hasTyped =
                    !string.IsNullOrWhiteSpace(summary) || !string.IsNullOrWhiteSpace(description) ||
                    !string.IsNullOrWhiteSpace(type) || !string.IsNullOrWhiteSpace(priority) ||
                    !string.IsNullOrWhiteSpace(assignee);
                var hasRaw = !string.IsNullOrWhiteSpace(jsonFile) || jsonStdin;

                if (hasTyped && hasRaw)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "Cannot combine typed options with --json-file/--json-stdin.");
                }

                if (!hasTyped && !hasRaw)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "Nothing to update: specify at least one typed option or use --json-file/--json-stdin.");
                }

                string body;
                if (hasRaw)
                {
                    body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                        ?? throw new TrackerException(ErrorCode.InvalidArgs, "Empty request body.");
                }
                else
                {
                    body = BuildTypedBody(summary, description, type, priority, assignee);
                }

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

    /// <summary>
    /// Строит JSON-тело для типизированного режима update. В теле окажутся только поля
    /// с непустыми значениями (PATCH-семантика: отсутствующее поле не трогаем).
    /// </summary>
    /// <param name="summary">Новый заголовок (необязательное).</param>
    /// <param name="description">Новое описание (необязательное).</param>
    /// <param name="type">Новый тип (необязательное).</param>
    /// <param name="priority">Новый приоритет (необязательное).</param>
    /// <param name="assignee">Новый исполнитель (необязательное).</param>
    /// <returns>Сериализованный компактный JSON-объект.</returns>
    private static string BuildTypedBody(
        string? summary,
        string? description,
        string? type,
        string? priority,
        string? assignee)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            if (!string.IsNullOrWhiteSpace(summary))
            {
                w.WriteString("summary", summary);
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                w.WriteString("description", description);
            }

            if (!string.IsNullOrWhiteSpace(type))
            {
                w.WriteString("type", type);
            }

            if (!string.IsNullOrWhiteSpace(priority))
            {
                w.WriteString("priority", priority);
            }

            if (!string.IsNullOrWhiteSpace(assignee))
            {
                w.WriteString("assignee", assignee);
            }

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
