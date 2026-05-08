namespace YandexTrackerCLI.Commands.Issue;

using System.CommandLine;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt issue create</c>: создаёт задачу (<c>POST /v3/issues</c>).
/// Тело собирается через <see cref="JsonBodyReader.ReadAndMerge"/>: scalar inline-флаги
/// (<c>--queue</c>, <c>--summary</c>, <c>--description</c>, <c>--type</c>,
/// <c>--priority</c>, <c>--assignee</c>) мерджатся поверх raw-payload.
/// Эффективное тело должно содержать <c>queue</c> и <c>summary</c>.
/// </summary>
public static class IssueCreateCommand
{
    /// <summary>
    /// Строит subcommand <c>create</c> для <c>yt issue</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var queueOpt = new Option<string?>("--queue") { Description = "Ключ очереди (override поля queue)." };
        var summaryOpt = new Option<string?>("--summary") { Description = "Заголовок задачи (override поля summary)." };
        var descriptionOpt = new Option<string?>("--description") { Description = "Описание (override поля description)." };
        var typeOpt = new Option<string?>("--type") { Description = "Тип задачи (override поля type)." };
        var priorityOpt = new Option<string?>("--priority") { Description = "Приоритет (override поля priority)." };
        var assigneeOpt = new Option<string?>("--assignee") { Description = "Исполнитель (override поля assignee)." };
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

                var overrides = new List<(string, JsonBodyMerger.OverrideValue)>();
                if (!string.IsNullOrWhiteSpace(queue))
                {
                    overrides.Add(("queue", JsonBodyMerger.OverrideValue.Of(queue!)));
                }
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
                        "Specify --json-file, --json-stdin, or inline flags.");

                using (var doc = JsonDocument.Parse(body))
                {
                    if (!doc.RootElement.TryGetProperty("queue", out _))
                    {
                        throw new TrackerException(ErrorCode.InvalidArgs,
                            "Effective body must include 'queue'.");
                    }
                    if (!doc.RootElement.TryGetProperty("summary", out _))
                    {
                        throw new TrackerException(ErrorCode.InvalidArgs,
                            "Effective body must include 'summary'.");
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
}
