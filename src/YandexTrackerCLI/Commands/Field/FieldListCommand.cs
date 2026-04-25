namespace YandexTrackerCLI.Commands.Field;

using System.CommandLine;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt field list</c>: выполняет <c>GET /v3/fields</c> (глобальные поля)
/// либо <c>GET /v3/queues/{queue}/localFields</c> при указании <c>--queue</c>
/// (локальные поля конкретной очереди) и печатает ответ сервера как есть.
/// Эндпоинт не поддерживает пагинацию — ответ целиком помещается в stdout.
/// </summary>
public static class FieldListCommand
{
    /// <summary>
    /// Строит subcommand <c>list</c> для <c>yt field</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var queueOpt = new Option<string?>("--queue")
        {
            Description = "Ключ очереди для локальных полей (если задан — вернутся только local fields этой очереди).",
        };

        var cmd = new Command("list", "Список полей задач (global или local для очереди).");
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

                var queue = pr.GetValue(queueOpt);
                var path = string.IsNullOrWhiteSpace(queue)
                    ? "fields"
                    : $"queues/{Uri.EscapeDataString(queue)}/localFields";

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
