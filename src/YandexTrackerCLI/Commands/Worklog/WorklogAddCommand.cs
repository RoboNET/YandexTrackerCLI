namespace YandexTrackerCLI.Commands.Worklog;

using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt worklog add &lt;issue-key&gt;</c>: добавляет запись учёта времени
/// (<c>POST /v3/issues/{key}/worklog</c>). Поддерживает два режима:
/// <list type="bullet">
///   <item><description>
///     <b>Typed</b> — через <c>--duration PT1H</c> (обязательный), плюс опционально
///     <c>--comment</c> и <c>--start &lt;ISO8601&gt;</c>. В теле запроса формируется
///     объект <c>{"duration":"...","comment":"...","start":"..."}</c>.
///   </description></item>
///   <item><description>
///     <b>Raw JSON</b> — через <c>--json-file &lt;path&gt;</c> или <c>--json-stdin</c>
///     (взаимоисключается с typed-опциями).
///   </description></item>
/// </list>
/// </summary>
public static class WorklogAddCommand
{
    /// <summary>
    /// Строит subcommand <c>add</c> для <c>yt worklog</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("issue-key") { Description = "Ключ задачи (например DEV-1)." };
        var durationOpt = new Option<string?>("--duration")
        {
            Description = "Длительность в формате ISO 8601 (например PT1H, PT30M, P1DT2H).",
        };
        var commentOpt = new Option<string?>("--comment") { Description = "Комментарий к записи учёта времени." };
        var startOpt = new Option<string?>("--start")
        {
            Description = "Время начала работы в формате ISO 8601 (например 2024-01-15T10:00:00+03:00).",
        };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу с телом запроса." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

        var cmd = new Command("add", "Добавить запись учёта времени (POST /v3/issues/{key}/worklog).");
        cmd.Arguments.Add(keyArg);
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
                        "Provide --duration (optionally --comment/--start) or --json-file/--json-stdin.");
                }

                string body;
                if (hasRaw)
                {
                    body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                        ?? throw new TrackerException(ErrorCode.InvalidArgs, "Empty request body.");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(duration))
                    {
                        throw new TrackerException(
                            ErrorCode.InvalidArgs,
                            "--duration is required in typed mode.");
                    }

                    ValidateIso8601Duration(duration);
                    if (!string.IsNullOrWhiteSpace(start))
                    {
                        ValidateIso8601DateTime(start);
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

                var result = await ctx.Client.PostJsonRawAsync(
                    $"issues/{Uri.EscapeDataString(key)}/worklog",
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
    /// Строит минимальное JSON-тело запроса для типизированного режима.
    /// </summary>
    /// <param name="duration">ISO 8601 длительность (обязательно).</param>
    /// <param name="comment">Опциональный комментарий.</param>
    /// <param name="start">Опциональное ISO 8601 время начала.</param>
    /// <returns>Сериализованный компактный JSON-объект.</returns>
    private static string BuildTypedBody(string duration, string? comment, string? start)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("duration", duration);
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

    /// <summary>
    /// Проверяет, что строка соответствует формату ISO 8601 duration
    /// (например <c>PT1H</c>, <c>PT30M</c>, <c>P1DT2H</c>).
    /// </summary>
    /// <param name="duration">Значение опции <c>--duration</c>.</param>
    /// <exception cref="TrackerException">
    /// Бросается с <see cref="ErrorCode.InvalidArgs"/>, если строка не парсится.
    /// </exception>
    internal static void ValidateIso8601Duration(string duration)
    {
        try
        {
            XmlConvert.ToTimeSpan(duration);
        }
        catch (FormatException)
        {
            throw new TrackerException(
                ErrorCode.InvalidArgs,
                $"--duration must be an ISO 8601 duration (e.g. PT1H, PT30M, P1DT2H), got '{duration}'.");
        }
    }

    /// <summary>
    /// Проверяет, что строка соответствует формату ISO 8601 date/time
    /// (парсится через <see cref="DateTimeOffset.TryParse(string, IFormatProvider, DateTimeStyles, out DateTimeOffset)"/>).
    /// </summary>
    /// <param name="value">Значение опции <c>--start</c>.</param>
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
                $"--start must be an ISO 8601 date/time (e.g. 2024-01-15T10:00:00+03:00), got '{value}'.");
        }
    }
}
