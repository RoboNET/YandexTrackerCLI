namespace YandexTrackerCLI.Commands.Worklog;

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt worklog update &lt;issue-key&gt; &lt;worklog-id&gt;</c>: обновляет
/// запись учёта времени (<c>PATCH /v3/issues/{key}/worklog/{id}</c>). Поддерживает
/// два режима:
/// <list type="bullet">
///   <item><description>
///     <b>Typed</b> — через <c>--duration</c>, <c>--comment</c>, <c>--start</c>
///     (все опциональны, но требуется хотя бы одно).
///   </description></item>
///   <item><description>
///     <b>Raw JSON</b> — через <c>--json-file &lt;path&gt;</c> или <c>--json-stdin</c>
///     (взаимоисключается с typed-опциями).
///   </description></item>
/// </list>
/// </summary>
public static class WorklogUpdateCommand
{
    /// <summary>
    /// Строит subcommand <c>update</c> для <c>yt worklog</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("issue-key") { Description = "Ключ задачи (например DEV-1)." };
        var idArg = new Argument<string>("worklog-id") { Description = "Идентификатор записи учёта времени." };
        var durationOpt = new Option<string?>("--duration")
        {
            Description = "Длительность в формате ISO 8601 (например PT1H, PT30M, P1DT2H).",
        };
        var commentOpt = new Option<string?>("--comment") { Description = "Новый комментарий." };
        var startOpt = new Option<string?>("--start")
        {
            Description = "Новое время начала работы в формате ISO 8601.",
        };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу с телом запроса." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

        var cmd = new Command("update", "Обновить запись учёта времени (PATCH /v3/issues/{key}/worklog/{id}).");
        cmd.Arguments.Add(keyArg);
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(durationOpt);
        cmd.Options.Add(commentOpt);
        cmd.Options.Add(startOpt);
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                var id = pr.GetValue(idArg)!;
                var duration = pr.GetValue(durationOpt);
                var comment = pr.GetValue(commentOpt);
                var start = pr.GetValue(startOpt);
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                var hasTyped = !string.IsNullOrWhiteSpace(duration)
                    || !string.IsNullOrWhiteSpace(comment)
                    || !string.IsNullOrWhiteSpace(start);
                var hasRaw = !string.IsNullOrWhiteSpace(jsonFile) || jsonStdin;

                if (hasTyped && hasRaw)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "Cannot combine --duration/--comment/--start with --json-file/--json-stdin.");
                }

                if (!hasTyped && !hasRaw)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "Provide at least one of --duration/--comment/--start or --json-file/--json-stdin.");
                }

                string body;
                if (hasRaw)
                {
                    body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                        ?? throw new TrackerException(ErrorCode.InvalidArgs, "Empty request body.");
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(duration))
                    {
                        WorklogAddCommand.ValidateIso8601Duration(duration);
                    }
                    if (!string.IsNullOrWhiteSpace(start))
                    {
                        WorklogAddCommand.ValidateIso8601DateTime(start);
                    }

                    body = BuildTypedBody(duration, comment, start);
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
                    $"issues/{Uri.EscapeDataString(key)}/worklog/{Uri.EscapeDataString(id)}",
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
    /// Строит JSON-тело запроса для типизированного update-режима, включая только
    /// заданные (non-null, non-whitespace) поля.
    /// </summary>
    /// <param name="duration">Опциональная ISO 8601 длительность.</param>
    /// <param name="comment">Опциональный комментарий.</param>
    /// <param name="start">Опциональное ISO 8601 время начала.</param>
    /// <returns>Сериализованный компактный JSON-объект.</returns>
    private static string BuildTypedBody(string? duration, string? comment, string? start)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            if (!string.IsNullOrWhiteSpace(duration))
            {
                w.WriteString("duration", duration);
            }
            if (!string.IsNullOrWhiteSpace(comment))
            {
                w.WriteString("comment", comment);
            }
            if (!string.IsNullOrWhiteSpace(start))
            {
                w.WriteString("start", start);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
