namespace YandexTrackerCLI.Commands.Version;

using System.CommandLine;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt version update &lt;id&gt;</c>: обновляет версию через
/// <c>PATCH /v3/versions/{id}</c>. Тело собирается через
/// <see cref="JsonBodyReader.ReadAndMerge"/>: scalar-флаги
/// (<c>--name</c>, <c>--description</c>, <c>--start-date</c>, <c>--due-date</c>,
/// <c>--released</c>) мерджатся поверх raw-payload.
/// </summary>
public static class VersionUpdateCommand
{
    /// <summary>
    /// Строит subcommand <c>update</c> для <c>yt version</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var idArg = new Argument<string>("id") { Description = "Идентификатор версии." };
        var nameOpt = new Option<string?>("--name") { Description = "Новое название (override поля name)." };
        var descriptionOpt = new Option<string?>("--description") { Description = "Новое описание (override поля description)." };
        var startDateOpt = new Option<string?>("--start-date") { Description = "Новая дата начала ISO 8601 (override поля startDate)." };
        var dueDateOpt = new Option<string?>("--due-date") { Description = "Новая дата завершения ISO 8601 (override поля dueDate)." };
        var releasedOpt = new Option<bool?>("--released") { Description = "Флаг released (override поля released)." };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу с телом запроса." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

        var cmd = new Command("update", "Обновить версию (PATCH /v3/versions/{id}).");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(nameOpt);
        cmd.Options.Add(descriptionOpt);
        cmd.Options.Add(startDateOpt);
        cmd.Options.Add(dueDateOpt);
        cmd.Options.Add(releasedOpt);
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var id = pr.GetValue(idArg)!;
                var name = pr.GetValue(nameOpt);
                var description = pr.GetValue(descriptionOpt);
                var startDate = pr.GetValue(startDateOpt);
                var dueDate = pr.GetValue(dueDateOpt);
                var released = pr.GetValue(releasedOpt);
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                if (!string.IsNullOrWhiteSpace(startDate))
                {
                    VersionDateValidator.ValidateIsoDate(startDate!, "--start-date");
                }
                if (!string.IsNullOrWhiteSpace(dueDate))
                {
                    VersionDateValidator.ValidateIsoDate(dueDate!, "--due-date");
                }

                var overrides = new List<(string, JsonBodyMerger.OverrideValue)>();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    overrides.Add(("name", JsonBodyMerger.OverrideValue.Of(name!)));
                }
                if (!string.IsNullOrWhiteSpace(description))
                {
                    overrides.Add(("description", JsonBodyMerger.OverrideValue.Of(description!)));
                }
                if (!string.IsNullOrWhiteSpace(startDate))
                {
                    overrides.Add(("startDate", JsonBodyMerger.OverrideValue.Of(startDate!)));
                }
                if (!string.IsNullOrWhiteSpace(dueDate))
                {
                    overrides.Add(("dueDate", JsonBodyMerger.OverrideValue.Of(dueDate!)));
                }
                if (released.HasValue)
                {
                    overrides.Add(("released", JsonBodyMerger.OverrideValue.Of(released.Value)));
                }

                var body = JsonBodyReader.ReadAndMerge(jsonFile, jsonStdin, Console.In, overrides)
                    ?? throw new TrackerException(ErrorCode.InvalidArgs,
                        "version update: nothing to update (provide typed flags or --json-file/--json-stdin).");

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.PatchJsonAsync(
                    $"versions/{Uri.EscapeDataString(id)}",
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
