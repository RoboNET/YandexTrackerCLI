namespace YandexTrackerCLI.Commands.Field;

using System.CommandLine;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt field get &lt;id&gt;</c>: выполняет <c>GET /v3/fields/{id}</c>
/// (глобальное поле) либо <c>GET /v3/queues/{queue}/localFields/{id}</c>
/// при указании <c>--queue</c> (локальное поле очереди) и печатает сырой JSON.
/// </summary>
public static class FieldGetCommand
{
    /// <summary>
    /// Строит subcommand <c>get</c> для <c>yt field</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var idArg = new Argument<string>("id") { Description = "Идентификатор (ключ) поля." };
        var queueOpt = new Option<string?>("--queue")
        {
            Description = "Ключ очереди для локального поля (если задан — запрашивается local field этой очереди).",
        };

        var cmd = new Command("get", "Получить поле по идентификатору (global или local для очереди).");
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
                var queue = pr.GetValue(queueOpt);
                var path = string.IsNullOrWhiteSpace(queue)
                    ? $"fields/{Uri.EscapeDataString(id)}"
                    : $"queues/{Uri.EscapeDataString(queue)}/localFields/{Uri.EscapeDataString(id)}";

                var result = await ctx.Client.GetAsync(path, ct);
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
