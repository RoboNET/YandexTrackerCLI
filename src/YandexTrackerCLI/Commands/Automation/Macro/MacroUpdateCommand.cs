namespace YandexTrackerCLI.Commands.Automation.Macro;

using System.CommandLine;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt automation macro update &lt;id&gt;</c>: выполняет
/// <c>PATCH /v3/queues/{queue}/macros/{id}</c>. Тело собирается из
/// источника (<c>--json-file</c> / <c>--json-stdin</c>) и inline-флага
/// <c>--name</c> через <see cref="JsonBodyReader.ReadAndMerge"/>.
/// </summary>
public static class MacroUpdateCommand
{
    /// <summary>
    /// Строит subcommand <c>update</c> для группы <c>yt automation macro</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var idArg = new Argument<string>("id") { Description = "Идентификатор макроса." };
        var queueOpt = new Option<string>("--queue") { Description = "Ключ очереди.", Required = true };
        var nameOpt = new Option<string?>("--name") { Description = "Override name." };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

        var cmd = new Command("update", "Обновить макрос (PATCH /v3/queues/{q}/macros/{id}).");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(queueOpt);
        cmd.Options.Add(nameOpt);
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var id = pr.GetValue(idArg)!;
                var queue = pr.GetValue(queueOpt)!;
                var name = pr.GetValue(nameOpt);

                var overrides = new List<(string, JsonBodyMerger.OverrideValue)>();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    overrides.Add(("name", JsonBodyMerger.OverrideValue.Of(name)));
                }

                var body = JsonBodyReader.ReadAndMerge(
                    pr.GetValue(jsonFileOpt), pr.GetValue(jsonStdinOpt), Console.In, overrides)
                    ?? throw new TrackerException(ErrorCode.InvalidArgs,
                        "Specify --json-file, --json-stdin, or inline flags.");

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.PatchJsonAsync(
                    $"queues/{Uri.EscapeDataString(queue)}/macros/{Uri.EscapeDataString(id)}",
                    body, ct);
                JsonWriter.Write(Console.Out, result, ctx.EffectiveOutputFormat,
                    pretty: !Console.IsOutputRedirected);
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
