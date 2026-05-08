namespace YandexTrackerCLI.Commands.Comment;

using System.CommandLine;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt comment add &lt;issue-key&gt;</c>: добавляет комментарий
/// (<c>POST /v3/issues/{key}/comments</c>). Тело собирается из источника
/// (<c>--json-file</c>/<c>--json-stdin</c>) и inline-флага <c>--text</c>
/// через <see cref="JsonBodyReader.ReadAndMerge"/>: inline-override
/// побеждает одноимённое поле в raw-payload.
/// </summary>
public static class CommentAddCommand
{
    /// <summary>
    /// Строит subcommand <c>add</c> для <c>yt comment</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("issue-key") { Description = "Ключ задачи (например DEV-1)." };
        var textOpt = new Option<string?>("--text") { Description = "Текст комментария (override поля text)." };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу с телом запроса." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

        var cmd = new Command("add", "Добавить комментарий (POST /v3/issues/{key}/comments).");
        cmd.Arguments.Add(keyArg);
        cmd.Options.Add(textOpt);
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                var text = pr.GetValue(textOpt);
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                var overrides = new List<(string, JsonBodyMerger.OverrideValue)>();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    overrides.Add(("text", JsonBodyMerger.OverrideValue.Of(text!)));
                }

                var body = JsonBodyReader.ReadAndMerge(jsonFile, jsonStdin, Console.In, overrides)
                    ?? throw new TrackerException(ErrorCode.InvalidArgs,
                        "Provide --text or --json-file/--json-stdin.");

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
                    $"issues/{Uri.EscapeDataString(key)}/comments",
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
