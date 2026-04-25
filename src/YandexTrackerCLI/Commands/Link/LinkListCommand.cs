namespace YandexTrackerCLI.Commands.Link;

using System.CommandLine;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt link list &lt;issue-key&gt;</c>: выполняет
/// <c>GET /v3/issues/{key}/links</c> и печатает JSON-ответ (массив связей) на stdout.
/// Связи задачи обычно немногочисленны, поэтому пагинация не используется — запрос
/// выполняется одним <see cref="YandexTrackerCLI.Core.Api.TrackerClient.GetAsync"/>.
/// </summary>
public static class LinkListCommand
{
    /// <summary>
    /// Строит subcommand <c>list</c> для <c>yt link</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("issue-key") { Description = "Ключ задачи (например DEV-1)." };

        var cmd = new Command("list", "Список связей задачи (GET /v3/issues/{key}/links).");
        cmd.Arguments.Add(keyArg);

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
                var key = pr.GetValue(keyArg)!;

                var result = await ctx.Client.GetAsync(
                    $"issues/{Uri.EscapeDataString(key)}/links",
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
