namespace YandexTrackerCLI.Commands.Project;

using System.CommandLine;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt project get &lt;id&gt;</c>: выполняет <c>GET /v3/entities/project/{id}</c>
/// и печатает сырой JSON с данными о проекте.
/// </summary>
public static class ProjectGetCommand
{
    /// <summary>
    /// Строит subcommand <c>get</c> для <c>yt project</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var idArg = new Argument<string>("id") { Description = "Идентификатор проекта." };
        var cmd = new Command("get", "Получить проект по идентификатору (GET /v3/entities/project/{id}).");
        cmd.Arguments.Add(idArg);
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
                    ct: ct);
                var id = parseResult.GetValue(idArg)!;
                var result = await ctx.Client.GetAsync($"entities/project/{Uri.EscapeDataString(id)}", ct);
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
