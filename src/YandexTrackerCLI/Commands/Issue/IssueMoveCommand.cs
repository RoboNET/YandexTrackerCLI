namespace YandexTrackerCLI.Commands.Issue;

using System.CommandLine;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt issue move &lt;key&gt;</c>: перемещение задачи в другую очередь
/// (<c>POST /v3/issues/{key}/_move</c>). Тело собирается через
/// <see cref="JsonBodyReader.ReadAndMerge"/>: scalar inline-флаг
/// <c>--to-queue</c> мерджится поверх raw-payload как поле <c>queue</c>.
/// Эффективное тело должно содержать <c>queue</c>.
/// </summary>
public static class IssueMoveCommand
{
    /// <summary>
    /// Строит subcommand <c>move</c> для <c>yt issue</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("key") { Description = "Ключ задачи (например DEV-1)." };

        var toQueueOpt = new Option<string?>("--to-queue") { Description = "Ключ целевой очереди (override поля queue)." };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу с телом запроса." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

        var cmd = new Command("move", "Переместить задачу в другую очередь (POST /v3/issues/{key}/_move).");
        cmd.Arguments.Add(keyArg);
        cmd.Options.Add(toQueueOpt);
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                var toQueue = pr.GetValue(toQueueOpt);
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                var overrides = new List<(string, JsonBodyMerger.OverrideValue)>();
                if (!string.IsNullOrWhiteSpace(toQueue))
                {
                    overrides.Add(("queue", JsonBodyMerger.OverrideValue.Of(toQueue!)));
                }

                var body = JsonBodyReader.ReadAndMerge(jsonFile, jsonStdin, Console.In, overrides)
                    ?? throw new TrackerException(ErrorCode.InvalidArgs,
                        "Specify --to-queue <QUEUE> or --json-file/--json-stdin.");

                using (var doc = JsonDocument.Parse(body))
                {
                    if (!doc.RootElement.TryGetProperty("queue", out _))
                    {
                        throw new TrackerException(ErrorCode.InvalidArgs,
                            "Effective body must include 'queue'.");
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

                var keyEsc = Uri.EscapeDataString(key);
                var result = await ctx.Client.PostJsonRawAsync($"issues/{keyEsc}/_move", body, ct);
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
