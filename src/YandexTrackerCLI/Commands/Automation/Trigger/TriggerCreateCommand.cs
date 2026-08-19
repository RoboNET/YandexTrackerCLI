namespace YandexTrackerCLI.Commands.Automation.Trigger;

using System.CommandLine;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt automation trigger create</c>: выполняет
/// <c>POST /v3/queues/{queue}/triggers/</c>. Тело собирается из источника
/// (<c>--json-file</c> / <c>--json-stdin</c>) и inline-флагов
/// (<c>--name</c>, <c>--active</c>, <c>--inactive</c>) через
/// <see cref="JsonBodyReader.ReadAndMerge"/>.
/// </summary>
public static class TriggerCreateCommand
{
    /// <summary>
    /// Строит subcommand <c>create</c> для группы <c>yt automation trigger</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var queueOpt = new Option<string>("--queue") { Description = "Ключ очереди.", Required = true };
        var nameOpt = new Option<string?>("--name") { Description = "Имя триггера (override)." };
        var activeOpt = new Option<bool>("--active") { Description = "Активный (override active=true)." };
        var inactiveOpt = new Option<bool>("--inactive") { Description = "Неактивный (override active=false)." };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

        var cmd = new Command("create", "Создать триггер (POST /v3/queues/{q}/triggers/).");
        cmd.Options.Add(queueOpt);
        cmd.Options.Add(nameOpt);
        cmd.Options.Add(activeOpt);
        cmd.Options.Add(inactiveOpt);
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var queue = pr.GetValue(queueOpt)!;
                var name = pr.GetValue(nameOpt);
                var active = pr.GetValue(activeOpt);
                var inactive = pr.GetValue(inactiveOpt);
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

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

                var body = JsonBodyReader.ReadAndMerge(jsonFile, jsonStdin, Console.In, overrides)
                    ?? throw new TrackerException(ErrorCode.InvalidArgs,
                        "Specify --json-file, --json-stdin, or inline flags.");

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.PostJsonRawAsync(
                    $"queues/{Uri.EscapeDataString(queue)}/triggers/", body, ct);
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
