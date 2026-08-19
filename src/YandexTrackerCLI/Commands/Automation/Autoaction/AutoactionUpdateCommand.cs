namespace YandexTrackerCLI.Commands.Automation.Autoaction;

using System.CommandLine;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt automation autoaction update &lt;id&gt;</c>: выполняет
/// <c>PATCH /v3/queues/{queue}/autoactions/{id}</c>. Тело собирается из
/// источника (<c>--json-file</c> / <c>--json-stdin</c>) и inline-флагов
/// (<c>--name</c>, <c>--active</c>, <c>--inactive</c>) через
/// <see cref="JsonBodyReader.ReadAndMerge"/>.
/// Опциональный <c>--version</c> добавляется query-параметром: API требует
/// версию (или <c>If-Match</c>) для PATCH.
/// Поле <c>version</c> из тела (его отдаёт GET, но PATCH его не принимает)
/// вырезается и, если флаг не задан, используется как значение query-параметра.
/// </summary>
public static class AutoactionUpdateCommand
{
    /// <summary>
    /// Строит subcommand <c>update</c> для группы <c>yt automation autoaction</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var idArg = new Argument<string>("id") { Description = "Идентификатор автодействия." };
        var queueOpt = new Option<string>("--queue") { Description = "Ключ очереди.", Required = true };
        var nameOpt = new Option<string?>("--name") { Description = "Override name." };
        var activeOpt = new Option<bool>("--active") { Description = "Override active=true." };
        var inactiveOpt = new Option<bool>("--inactive") { Description = "Override active=false." };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };
        var versionOpt = AutomationVersionOption.Create("автодействия");

        var cmd = new Command("update", "Обновить автодействие (PATCH /v3/queues/{q}/autoactions/{id}).");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(queueOpt);
        cmd.Options.Add(nameOpt);
        cmd.Options.Add(activeOpt);
        cmd.Options.Add(inactiveOpt);
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);
        cmd.Options.Add(versionOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var id = pr.GetValue(idArg)!;
                var queue = pr.GetValue(queueOpt)!;
                var name = pr.GetValue(nameOpt);
                var active = pr.GetValue(activeOpt);
                var inactive = pr.GetValue(inactiveOpt);
                var version = pr.GetValue(versionOpt);

                if (active && inactive)
                {
                    throw new TrackerException(ErrorCode.InvalidArgs,
                        "--active and --inactive are mutually exclusive.");
                }

                var overrides = new List<(string, JsonBodyMerger.OverrideValue)>();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    overrides.Add(("name", JsonBodyMerger.OverrideValue.Of(name)));
                }
                if (active)
                {
                    overrides.Add(("active", JsonBodyMerger.OverrideValue.Of(true)));
                }
                else if (inactive)
                {
                    overrides.Add(("active", JsonBodyMerger.OverrideValue.Of(false)));
                }

                var body = JsonBodyReader.ReadAndMerge(
                    pr.GetValue(jsonFileOpt), pr.GetValue(jsonStdinOpt), Console.In, overrides)
                    ?? throw new TrackerException(ErrorCode.InvalidArgs,
                        "Specify --json-file, --json-stdin, or inline flags.");
                // Поле version приходит из GET, но в теле PATCH запрещено:
                // вырезаем его и, если явного флага нет, используем как версию запроса.
                body = AutomationVersionExtractor.StripVersion(body, out var bodyVersion);

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var path = AutomationVersionOption.AppendVersionQuery(
                    $"queues/{Uri.EscapeDataString(queue)}/autoactions/{Uri.EscapeDataString(id)}",
                    version ?? bodyVersion);

                var result = await ctx.Client.PatchJsonAsync(path, body, ct);
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
