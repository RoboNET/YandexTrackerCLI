namespace YandexTrackerCLI.Commands.Automation.Macro;

using System.CommandLine;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt automation macro get &lt;id&gt; --queue &lt;key&gt;</c>:
/// выполняет <c>GET /v3/queues/{queue}/macros/{id}</c> и печатает
/// JSON-описание макроса на stdout.
/// </summary>
public static class MacroGetCommand
{
    /// <summary>
    /// Строит subcommand <c>get</c> для группы <c>yt automation macro</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var idArg = new Argument<string>("id") { Description = "Идентификатор макроса." };
        var queueOpt = new Option<string>("--queue") { Description = "Ключ очереди.", Required = true };

        var cmd = new Command("get", "Получить макрос (GET /v3/queues/{q}/macros/{id}).");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(queueOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var id = pr.GetValue(idArg)!;
                var queue = pr.GetValue(queueOpt)!;
                var result = await ctx.Client.GetAsync(
                    $"queues/{Uri.EscapeDataString(queue)}/macros/{Uri.EscapeDataString(id)}", ct);

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
