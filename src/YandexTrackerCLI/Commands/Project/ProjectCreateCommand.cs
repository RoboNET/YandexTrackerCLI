namespace YandexTrackerCLI.Commands.Project;

using System.CommandLine;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt project create --json-file &lt;path&gt;</c> (или <c>--json-stdin</c>):
/// создаёт проект через <c>POST /v3/entities/project</c>. Поскольку payload проекта
/// слишком гибкий для typed-флагов, поддерживается только raw-режим — обязательно
/// указать источник тела запроса.
/// </summary>
public static class ProjectCreateCommand
{
    /// <summary>
    /// Строит subcommand <c>create</c> для <c>yt project</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var jsonFileOpt = new Option<string?>("--json-file")
        {
            Description = "Путь к JSON-файлу с телом запроса (обязательно для create).",
        };
        var jsonStdinOpt = new Option<bool>("--json-stdin")
        {
            Description = "Читать JSON-тело из stdin (альтернатива --json-file).",
        };

        var cmd = new Command("create", "Создать проект (POST /v3/entities/project).");
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                var body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                    ?? throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "project create requires --json-file or --json-stdin.");

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.PostJsonRawAsync("entities/project", body, ct);
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
