namespace YandexTrackerCLI.Commands.Component;

using System.CommandLine;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt component get &lt;id&gt;</c>: выполняет <c>GET /v3/components/{id}</c>
/// и печатает сырой JSON с данными о компоненте.
/// </summary>
public static class ComponentGetCommand
{
    /// <summary>
    /// Строит subcommand <c>get</c> для <c>yt component</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var idArg = new Argument<string>("id") { Description = "Идентификатор компонента." };
        var cmd = new Command("get", "Получить компонент по идентификатору (GET /v3/components/{id}).");
        cmd.Arguments.Add(idArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            try
            {
                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: parseResult.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: parseResult.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: parseResult.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: parseResult.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !parseResult.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: parseResult.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);
                var id = parseResult.GetValue(idArg)!;
                // Компонент адресуется идентификатором, очереди в URL нет — при действующем
                // allowed_queues доспрашиваем владельца и сверяем со списком. Полученное
                // представление и есть ответ команды, второй запрос не нужен.
                var result = await QueueScopeFilter.EnsureResourceQueueAllowed(
                        ctx.Client, "components", id, ctx.Profile, ct)
                    ?? await ctx.Client.GetAsync($"components/{Uri.EscapeDataString(id)}", ct);
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
