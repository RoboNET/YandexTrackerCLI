namespace YandexTrackerCLI.Commands.Project;

using System.CommandLine;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt project update &lt;id&gt;</c>: обновляет проект через
/// <c>PATCH /v3/entities/project/{id}</c>. Как и <c>create</c>, поддерживает только
/// raw-режим — обязательно указать <c>--json-file</c> или <c>--json-stdin</c>.
/// </summary>
public static class ProjectUpdateCommand
{
    /// <summary>
    /// Строит subcommand <c>update</c> для <c>yt project</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var idArg = new Argument<string>("id") { Description = "Идентификатор проекта." };
        var jsonFileOpt = new Option<string?>("--json-file")
        {
            Description = "Путь к JSON-файлу с телом запроса (обязательно для update).",
        };
        var jsonStdinOpt = new Option<bool>("--json-stdin")
        {
            Description = "Читать JSON-тело из stdin (альтернатива --json-file).",
        };

        var cmd = new Command("update", "Обновить проект (PATCH /v3/entities/project/{id}).");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var id = pr.GetValue(idArg)!;
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                var body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                    ?? throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "project update requires --json-file or --json-stdin.");

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.PatchJsonAsync(
                    $"entities/project/{Uri.EscapeDataString(id)}",
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
