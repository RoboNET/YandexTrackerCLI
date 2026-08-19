namespace YandexTrackerCLI.Commands.Automation.Trigger;

using System.CommandLine;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt automation trigger activate &lt;id&gt; --queue &lt;key&gt;</c>:
/// выполняет <c>PATCH /v3/queues/{queue}/triggers/{id}</c> с фиксированным
/// телом <c>{"active":true}</c>. Отдельная сборка тела (без merge) гарантирует,
/// что значение поля <c>active</c> не подменяется пользовательскими override'ами.
/// Опциональный <c>--version</c> добавляется query-параметром: API требует
/// версию (или <c>If-Match</c>) для PATCH. Без явного флага подставляется версия,
/// запомненная предыдущим <c>get</c>; <c>--no-version-check</c> её игнорирует,
/// <c>--overwrite-latest</c> перечитывает текущую.
/// </summary>
public static class TriggerActivateCommand
{
    /// <summary>
    /// Строит subcommand <c>activate</c> для группы <c>yt automation trigger</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build() => BuildSetActive(true,
        "activate", "Активировать триггер (PATCH active=true).");

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
        var idArg = new Argument<string>("id") { Description = "Идентификатор триггера." };
        var queueOpt = new Option<string>("--queue") { Description = "Ключ очереди.", Required = true };
        var versionOpt = ResourceVersionOption.Create("триггера");
        var noVersionCheckOpt = ResourceVersionOption.CreateNoVersionCheck();
        var overwriteLatestOpt = ResourceVersionOption.CreateOverwriteLatest("триггер");

        var cmd = new Command(verb, desc);
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(queueOpt);
        cmd.Options.Add(versionOpt);
        cmd.Options.Add(noVersionCheckOpt);
        cmd.Options.Add(overwriteLatestOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            var decision = new ResourceVersionDecision(null, ResourceVersionSource.None, null);
            var id = pr.GetValue(idArg)!;
            var queue = pr.GetValue(queueOpt)!;
            try
            {
                var explicitVersion = pr.GetValue(versionOpt);
                var overwriteLatest = pr.GetValue(overwriteLatestOpt);
                ResourceVersionFlow.EnsureFlagsCompatible(explicitVersion, overwriteLatest);

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var body = target ? """{"active":true}""" : """{"active":false}""";
                var path = $"queues/{Uri.EscapeDataString(queue)}/triggers/{Uri.EscapeDataString(id)}";
                decision = await ResourceVersionFlow.Resolve(
                    ctx, ResourceVersionFlow.TriggerResource, $"{queue}/{id}", path,
                    explicitVersion, bodyVersion: null,
                    pr.GetValue(noVersionCheckOpt), overwriteLatest, ct);

                var result = await ctx.Client.PatchJsonAsync(
                    ResourceVersionOption.AppendVersionQuery(path, decision.Version), body, ct);

                // Новая версия из ответа — иначе activate и следующий update подряд
                // упёрлись бы в конфликт.
                await ResourceVersionFlow.Remember(
                    ctx, ResourceVersionFlow.TriggerResource, $"{queue}/{id}", result, ct);

                JsonWriter.Write(Console.Out, result, ctx.EffectiveOutputFormat,
                    pretty: !Console.IsOutputRedirected);
                return 0;
            }
            catch (TrackerException ex)
            {
                var explained = ResourceVersionFlow.Explain(
                    ex, decision, $"yt automation trigger get {id} --queue {queue}");
                ErrorWriter.Write(Console.Error, explained);
                return explained.Code.ToExitCode();
            }
        });

        return cmd;
    }
}
