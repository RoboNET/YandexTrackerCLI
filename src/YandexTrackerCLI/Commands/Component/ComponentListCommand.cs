namespace YandexTrackerCLI.Commands.Component;

using System.CommandLine;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt component list --queue &lt;key&gt;</c>: выполняет
/// <c>GET /v3/queues/{queue}/components</c> и печатает ответ сервера как есть
/// (API возвращает JSON-массив компонентов очереди). Эндпоинт не поддерживает
/// пагинацию — ответ целиком помещается в stdout.
/// </summary>
public static class ComponentListCommand
{
    /// <summary>
    /// Строит subcommand <c>list</c> для <c>yt component</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var queueOpt = new Option<string>("--queue")
        {
            Description = "Ключ очереди (обязательно).",
            Required = true,
        };

        var cmd = new Command("list", "Список компонентов очереди (GET /v3/queues/{queue}/components).");
        cmd.Options.Add(queueOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var queue = pr.GetValue(queueOpt)!;
                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.GetAsync(
                    $"queues/{Uri.EscapeDataString(queue)}/components",
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
