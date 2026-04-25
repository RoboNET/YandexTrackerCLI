namespace YandexTrackerCLI.Commands.Issue;

using System.CommandLine;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt issue transition &lt;key&gt;</c>: просмотр доступных переходов
/// (<c>GET /v3/issues/{key}/transitions</c>) либо выполнение конкретного перехода
/// (<c>POST /v3/issues/{key}/transitions/{id}/_execute</c>).
/// </summary>
/// <remarks>
/// Режимы работы:
/// <list type="bullet">
///   <item><description>
///     <c>--list</c> — отправить GET и вывести список доступных переходов. Если одновременно
///     указан <c>--to</c>, он игнорируется (<c>--list</c> имеет приоритет).
///   </description></item>
///   <item><description>
///     <c>--to &lt;id&gt;</c> — выполнить переход. Тело запроса по умолчанию <c>{}</c>,
///     но можно передать <c>--json-file</c> / <c>--json-stdin</c> для полей вроде
///     <c>resolution</c> или <c>comment</c>.
///   </description></item>
/// </list>
/// Если не указан ни <c>--list</c>, ни <c>--to</c>, команда завершится с кодом 2
/// (<see cref="ErrorCode.InvalidArgs"/>).
/// </remarks>
public static class IssueTransitionCommand
{
    /// <summary>
    /// Строит subcommand <c>transition</c> для <c>yt issue</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("key") { Description = "Ключ задачи (например DEV-1)." };

        var toOpt = new Option<string?>("--to") { Description = "ID перехода для выполнения." };
        var listOpt = new Option<bool>("--list") { Description = "Показать доступные переходы (GET)." };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу с телом запроса (для _execute)." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin (для _execute)." };

        var cmd = new Command("transition", "Переходы задачи: list / execute.");
        cmd.Arguments.Add(keyArg);
        cmd.Options.Add(toOpt);
        cmd.Options.Add(listOpt);
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                var to = pr.GetValue(toOpt);
                var list = pr.GetValue(listOpt);
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                if (!list && string.IsNullOrWhiteSpace(to))
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "Specify --to <transition-id> or --list.");
                }

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var keyEsc = Uri.EscapeDataString(key);

                if (list)
                {
                    var result = await ctx.Client.GetAsync($"issues/{keyEsc}/transitions", ct);
                    JsonWriter.Write(Console.Out, result, ctx.EffectiveOutputFormat, pretty: !Console.IsOutputRedirected);
                    return 0;
                }

                var body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In) ?? "{}";
                var toEsc = Uri.EscapeDataString(to!);
                var resp = await ctx.Client.PostJsonRawAsync($"issues/{keyEsc}/transitions/{toEsc}/_execute", body, ct);
                JsonWriter.Write(Console.Out, resp, ctx.EffectiveOutputFormat, pretty: !Console.IsOutputRedirected);
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
