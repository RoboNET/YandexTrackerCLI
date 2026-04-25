namespace YandexTrackerCLI.Commands.Issue;

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt issue move &lt;key&gt;</c>: перемещение задачи в другую очередь
/// (<c>POST /v3/issues/{key}/_move</c>).
/// </summary>
/// <remarks>
/// Режимы формирования тела запроса:
/// <list type="bullet">
///   <item><description>
///     <c>--to-queue &lt;QUEUE&gt;</c> — построить typed body вида <c>{"queue":"QUEUE"}</c>.
///   </description></item>
///   <item><description>
///     <c>--json-file &lt;path&gt;</c> или <c>--json-stdin</c> — использовать произвольное
///     raw JSON-тело (позволяет передать дополнительные поля вроде <c>notify</c>).
///   </description></item>
/// </list>
/// Одновременное использование <c>--to-queue</c> и <c>--json-file</c>/<c>--json-stdin</c>
/// либо отсутствие обоих завершит команду с кодом 2 (<see cref="ErrorCode.InvalidArgs"/>).
/// </remarks>
public static class IssueMoveCommand
{
    /// <summary>
    /// Строит subcommand <c>move</c> для <c>yt issue</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("key") { Description = "Ключ задачи (например DEV-1)." };

        var toQueueOpt = new Option<string?>("--to-queue") { Description = "Ключ целевой очереди (например NEWQ)." };
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

                var hasTyped = !string.IsNullOrWhiteSpace(toQueue);
                var hasRaw = !string.IsNullOrWhiteSpace(jsonFile) || jsonStdin;

                if (hasTyped && hasRaw)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "Cannot combine --to-queue with --json-file/--json-stdin.");
                }

                if (!hasTyped && !hasRaw)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "Specify --to-queue <QUEUE> or --json-file/--json-stdin.");
                }

                string body;
                if (hasRaw)
                {
                    body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                        ?? throw new TrackerException(ErrorCode.InvalidArgs, "Empty request body.");
                }
                else
                {
                    using var ms = new MemoryStream();
                    using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
                    {
                        w.WriteStartObject();
                        w.WriteString("queue", toQueue!);
                        w.WriteEndObject();
                    }

                    body = Encoding.UTF8.GetString(ms.ToArray());
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
