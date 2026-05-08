namespace YandexTrackerCLI.Commands.Automation.Autoaction;

using System.CommandLine;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt automation autoaction activate &lt;id&gt; --queue &lt;key&gt;</c>:
/// выполняет <c>PATCH /v3/queues/{queue}/autoactions/{id}</c> с фиксированным
/// телом <c>{"active":true}</c>. Отдельная сборка тела (без merge) гарантирует,
/// что значение поля <c>active</c> не подменяется пользовательскими override'ами.
/// </summary>
public static class AutoactionActivateCommand
{
    /// <summary>
    /// Строит subcommand <c>activate</c> для группы <c>yt automation autoaction</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build() => BuildSetActive(true,
        "activate", "Активировать автодействие (PATCH active=true).");

    /// <summary>
    /// Общая фабрика для <c>activate</c> и <c>deactivate</c>: формирует
    /// <see cref="Command"/> с одинаковой формой аргументов и фиксированным
    /// PATCH-телом, зависящим от целевого значения <paramref name="target"/>.
    /// </summary>
    /// <param name="target">Целевое значение поля <c>active</c>.</param>
    /// <param name="verb">Имя CLI-подкоманды (<c>activate</c> либо <c>deactivate</c>).</param>
    /// <param name="desc">Текст описания подкоманды.</param>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    internal static Command BuildSetActive(bool target, string verb, string desc)
    {
        var idArg = new Argument<string>("id") { Description = "Идентификатор автодействия." };
        var queueOpt = new Option<string>("--queue") { Description = "Ключ очереди.", Required = true };

        var cmd = new Command(verb, desc);
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
                var body = target ? """{"active":true}""" : """{"active":false}""";
                var result = await ctx.Client.PatchJsonAsync(
                    $"queues/{Uri.EscapeDataString(queue)}/autoactions/{Uri.EscapeDataString(id)}",
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
