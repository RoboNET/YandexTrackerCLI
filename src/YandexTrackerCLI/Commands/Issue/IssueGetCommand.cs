namespace YandexTrackerCLI.Commands.Issue;

using System.CommandLine;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt issue get &lt;key&gt;</c>: выполняет <c>GET /v3/issues/{key}</c>
/// и печатает данные о задаче.
/// </summary>
/// <remarks>
/// Для форматов <see cref="OutputFormat.Json"/> и <see cref="OutputFormat.Minimal"/> — стандартный
/// диспатч через <see cref="JsonWriter.Write"/>. Для <see cref="OutputFormat.Table"/> в TTY
/// рендерится rich detail view с markdown-описанием через <see cref="IssueDetailRenderer"/>;
/// при необходимости вывод проходит через <see cref="PagerWriter"/>.
/// </remarks>
public static class IssueGetCommand
{
    /// <summary>
    /// Строит subcommand <c>get</c> для <c>yt issue</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("key") { Description = "Ключ задачи, например DEV-1." };
        var cmd = new Command("get", "Получить задачу по ключу (GET /v3/issues/{key}).");
        cmd.Arguments.Add(keyArg);
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
                    cliNoColor: parseResult.GetValue(RootCommandBuilder.NoColorOption),
                    cliNoPager: parseResult.GetValue(RootCommandBuilder.NoPagerOption),
                    ct: ct);
                var key = parseResult.GetValue(keyArg)!;
                var result = await ctx.Client.GetAsync($"issues/{Uri.EscapeDataString(key)}", ct);

                if (ctx.EffectiveOutputFormat == OutputFormat.Table)
                {
                    using var pager = PagerWriter.Create(ctx.TerminalCapabilities, Console.Out);
                    IssueDetailRenderer.Render(pager, result, ctx.TerminalCapabilities);
                    pager.Flush();
                }
                else
                {
                    JsonWriter.Write(Console.Out, result, ctx.EffectiveOutputFormat, pretty: !Console.IsOutputRedirected);
                }
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
