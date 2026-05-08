namespace YandexTrackerCLI.Commands.Worklog;

using System.CommandLine;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt worklog update &lt;issue-key&gt; &lt;worklog-id&gt;</c>: обновляет
/// запись учёта времени (<c>PATCH /v3/issues/{key}/worklog/{id}</c>). Тело собирается
/// из источника (<c>--json-file</c>/<c>--json-stdin</c>) и inline-флагов
/// (<c>--duration</c>, <c>--comment</c>, <c>--start</c>) через
/// <see cref="JsonBodyReader.ReadAndMerge"/>.
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

                if (!string.IsNullOrWhiteSpace(duration))
                {
                    WorklogAddCommand.ValidateIso8601Duration(duration!);
                }
                if (!string.IsNullOrWhiteSpace(start))
                {
                    WorklogAddCommand.ValidateIso8601DateTime(start!);
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
                        "Provide at least one of --duration/--comment/--start or --json-file/--json-stdin.");

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
}
