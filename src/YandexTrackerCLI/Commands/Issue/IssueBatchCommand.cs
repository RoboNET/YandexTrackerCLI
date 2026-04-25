namespace YandexTrackerCLI.Commands.Issue;

using System.CommandLine;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt issue batch</c>: пакетное изменение задач через
/// <c>POST /v3/bulkchange</c>. Тело запроса обязательно передаётся в raw-формате
/// (<c>--json-file</c> либо <c>--json-stdin</c>) — batch-payload слишком гибкий,
/// чтобы отражать его typed-аргументами.
/// </summary>
/// <remarks>
/// Если ни <c>--json-file</c>, ни <c>--json-stdin</c> не указаны, команда завершится
/// с кодом 2 (<see cref="ErrorCode.InvalidArgs"/>).
/// </remarks>
public static class IssueBatchCommand
{
    /// <summary>
    /// Строит subcommand <c>batch</c> для <c>yt issue</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу batch-операций." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать batch-тело из stdin." };

        var cmd = new Command("batch", "Пакетное изменение задач (POST /v3/bulkchange).");
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                var body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                    ?? throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "Batch requires --json-file or --json-stdin with the operations payload.");

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.PostJsonRawAsync("bulkchange", body, ct);
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
