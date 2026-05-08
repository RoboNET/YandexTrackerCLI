namespace YandexTrackerCLI.Commands.Worklog;

using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Xml;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt worklog add &lt;issue-key&gt;</c>: добавляет запись учёта времени
/// (<c>POST /v3/issues/{key}/worklog</c>). Тело собирается из источника
/// (<c>--json-file</c>/<c>--json-stdin</c>) и inline-флагов
/// (<c>--duration</c>, <c>--comment</c>, <c>--start</c>) через
/// <see cref="JsonBodyReader.ReadAndMerge"/>: inline-override побеждает
/// одноимённое поле в raw-payload. Эффективное тело должно содержать
/// поле <c>duration</c>.
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

                if (!string.IsNullOrWhiteSpace(duration))
                {
                    ValidateIso8601Duration(duration!);
                }
                if (!string.IsNullOrWhiteSpace(start))
                {
                    ValidateIso8601DateTime(start!);
                }

                var overrides = new List<(string, JsonBodyMerger.OverrideValue)>();
                if (!string.IsNullOrWhiteSpace(duration))
                {
                    overrides.Add(("duration", JsonBodyMerger.OverrideValue.Of(duration!)));
                }
                if (!string.IsNullOrWhiteSpace(comment))
                {
                    overrides.Add(("comment", JsonBodyMerger.OverrideValue.Of(comment!)));
                }
                if (!string.IsNullOrWhiteSpace(start))
                {
                    overrides.Add(("start", JsonBodyMerger.OverrideValue.Of(start!)));
                }

                var body = JsonBodyReader.ReadAndMerge(jsonFile, jsonStdin, Console.In, overrides)
                    ?? throw new TrackerException(ErrorCode.InvalidArgs,
                        "Provide --duration (optionally --comment/--start) or --json-file/--json-stdin.");

                using (var doc = JsonDocument.Parse(body))
                {
                    if (!doc.RootElement.TryGetProperty("duration", out _))
                    {
                        throw new TrackerException(ErrorCode.InvalidArgs,
                            "Effective body must include 'duration'.");
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
