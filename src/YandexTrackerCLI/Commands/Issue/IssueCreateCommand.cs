namespace YandexTrackerCLI.Commands.Issue;

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt issue create</c>: создаёт задачу (<c>POST /v3/issues</c>).
/// Поддерживает два режима задания payload:
/// <list type="bullet">
///   <item><description>
///     <b>Typed</b> — через флаги <c>--queue</c>, <c>--summary</c>, <c>--description</c>,
///     <c>--type</c>, <c>--priority</c>, <c>--assignee</c>. <c>--queue</c> и <c>--summary</c>
///     обязательны в этом режиме.
///   </description></item>
///   <item><description>
///     <b>Raw JSON</b> — через <c>--json-file &lt;path&gt;</c> или <c>--json-stdin</c>
///     (взаимоисключается с typed-флагами).
///   </description></item>
/// </list>
/// </summary>
public static class IssueCreateCommand
{
    /// <summary>
    /// Строит subcommand <c>create</c> для <c>yt issue</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var queueOpt = new Option<string?>("--queue") { Description = "Ключ очереди (например DEV)." };
        var summaryOpt = new Option<string?>("--summary") { Description = "Заголовок задачи." };
        var descriptionOpt = new Option<string?>("--description") { Description = "Описание." };
        var typeOpt = new Option<string?>("--type") { Description = "Тип задачи (bug, task, ...)." };
        var priorityOpt = new Option<string?>("--priority") { Description = "Приоритет." };
        var assigneeOpt = new Option<string?>("--assignee") { Description = "Исполнитель." };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу с телом запроса." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

        var cmd = new Command("create", "Создать задачу (POST /v3/issues).");
        cmd.Options.Add(queueOpt);
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
                var queue = pr.GetValue(queueOpt);
                var summary = pr.GetValue(summaryOpt);
                var description = pr.GetValue(descriptionOpt);
                var type = pr.GetValue(typeOpt);
                var priority = pr.GetValue(priorityOpt);
                var assignee = pr.GetValue(assigneeOpt);
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                var hasTyped =
                    !string.IsNullOrWhiteSpace(queue) || !string.IsNullOrWhiteSpace(summary) ||
                    !string.IsNullOrWhiteSpace(description) || !string.IsNullOrWhiteSpace(type) ||
                    !string.IsNullOrWhiteSpace(priority) || !string.IsNullOrWhiteSpace(assignee);
                var hasRaw = !string.IsNullOrWhiteSpace(jsonFile) || jsonStdin;

                if (hasTyped && hasRaw)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "Cannot combine typed options (--queue/--summary/...) with --json-file/--json-stdin.");
                }

                string body;
                if (hasRaw)
                {
                    body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                        ?? throw new TrackerException(ErrorCode.InvalidArgs, "Empty request body.");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(queue) || string.IsNullOrWhiteSpace(summary))
                    {
                        throw new TrackerException(
                            ErrorCode.InvalidArgs,
                            "--queue and --summary are required unless --json-file/--json-stdin is used.");
                    }

                    body = BuildTypedBody(queue, summary, description, type, priority, assignee);
                }

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.PostJsonRawAsync("issues", body, ct);
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
    /// Строит минимальное JSON-тело запроса для типизированного режима.
    /// Опциональные поля добавляются только при наличии непустого значения.
    /// </summary>
    /// <param name="queue">Ключ очереди (обязательное поле).</param>
    /// <param name="summary">Заголовок задачи (обязательное поле).</param>
    /// <param name="description">Описание задачи (необязательное).</param>
    /// <param name="type">Тип задачи (необязательное).</param>
    /// <param name="priority">Приоритет (необязательное).</param>
    /// <param name="assignee">Исполнитель (необязательное).</param>
    /// <returns>Сериализованный компактный JSON-объект.</returns>
    private static string BuildTypedBody(
        string queue,
        string summary,
        string? description,
        string? type,
        string? priority,
        string? assignee)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("queue", queue);
            w.WriteString("summary", summary);
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
