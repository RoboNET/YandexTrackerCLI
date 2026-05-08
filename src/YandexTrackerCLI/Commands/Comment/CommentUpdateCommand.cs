namespace YandexTrackerCLI.Commands.Comment;

using System.CommandLine;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt comment update &lt;issue-key&gt; &lt;comment-id&gt;</c>:
/// обновляет комментарий (<c>PATCH /v3/issues/{key}/comments/{id}</c>).
/// Тело собирается из источника (<c>--json-file</c>/<c>--json-stdin</c>)
/// и inline-флага <c>--text</c> через <see cref="JsonBodyReader.ReadAndMerge"/>.
/// </summary>
public static class CommentUpdateCommand
{
    /// <summary>
    /// Строит subcommand <c>update</c> для <c>yt comment</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("issue-key") { Description = "Ключ задачи (например DEV-1)." };
        var idArg = new Argument<string>("comment-id") { Description = "Идентификатор комментария." };
        var textOpt = new Option<string?>("--text") { Description = "Новый текст комментария (override поля text)." };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу с телом запроса." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

        var cmd = new Command("update", "Обновить комментарий (PATCH /v3/issues/{key}/comments/{id}).");
        cmd.Arguments.Add(keyArg);
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(textOpt);
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                var id = pr.GetValue(idArg)!;
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

                var result = await ctx.Client.PatchJsonAsync(
                    $"issues/{Uri.EscapeDataString(key)}/comments/{Uri.EscapeDataString(id)}",
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
