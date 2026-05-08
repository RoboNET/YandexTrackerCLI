namespace YandexTrackerCLI.Commands.Link;

using System.CommandLine;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt link add &lt;issue-key&gt;</c>: добавляет связь
/// (<c>POST /v3/issues/{key}/links</c>). Тело собирается из источника
/// (<c>--json-file</c>/<c>--json-stdin</c>) и inline-флагов
/// (<c>--to</c>, <c>--type</c>) через <see cref="JsonBodyReader.ReadAndMerge"/>:
/// inline-override побеждает одноимённые поля. Эффективное тело должно
/// содержать <c>relationship</c> и <c>issue</c>.
/// </summary>
public static class LinkAddCommand
{
    /// <summary>
    /// Строит subcommand <c>add</c> для <c>yt link</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("issue-key") { Description = "Ключ задачи (например DEV-1)." };
        var toOpt = new Option<string?>("--to") { Description = "Ключ связанной задачи (override поля issue)." };
        var typeOpt = new Option<string?>("--type") { Description = "Тип связи (override поля relationship)." };
        typeOpt.AcceptOnlyFromAmong(
            "relates",
            "is-dependent-by",
            "depends-on",
            "is-subtask-of",
            "subtasks",
            "duplicates",
            "is-duplicated-by",
            "is-epic-of",
            "has-epic");
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу с телом запроса." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

        var cmd = new Command(
            "add",
            "Добавить связь к задаче (POST /v3/issues/{key}/links).");
        cmd.Arguments.Add(keyArg);
        cmd.Options.Add(toOpt);
        cmd.Options.Add(typeOpt);
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                var toKey = pr.GetValue(toOpt);
                var type = pr.GetValue(typeOpt);
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                var overrides = new List<(string, JsonBodyMerger.OverrideValue)>();
                if (!string.IsNullOrWhiteSpace(type))
                {
                    overrides.Add(("relationship", JsonBodyMerger.OverrideValue.Of(type!)));
                }
                if (!string.IsNullOrWhiteSpace(toKey))
                {
                    overrides.Add(("issue", JsonBodyMerger.OverrideValue.Of(toKey!)));
                }

                var body = JsonBodyReader.ReadAndMerge(jsonFile, jsonStdin, Console.In, overrides)
                    ?? throw new TrackerException(ErrorCode.InvalidArgs,
                        "Provide --to and --type, or --json-file/--json-stdin.");

                using (var doc = JsonDocument.Parse(body))
                {
                    if (!doc.RootElement.TryGetProperty("relationship", out _))
                    {
                        throw new TrackerException(ErrorCode.InvalidArgs,
                            "Effective body must include 'relationship'.");
                    }
                    if (!doc.RootElement.TryGetProperty("issue", out _))
                    {
                        throw new TrackerException(ErrorCode.InvalidArgs,
                            "Effective body must include 'issue'.");
                    }
                }

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.PostJsonRawAsync(
                    $"issues/{Uri.EscapeDataString(key)}/links",
                    body,
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
