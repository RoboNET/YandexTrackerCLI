namespace YandexTrackerCLI.Commands.Ref;

using System.CommandLine;
using Core.Api.Errors;
using Output;

/// <summary>
/// Внутренняя фабрика для справочных (read-only) команд группы <c>yt ref</c>:
/// все такие команды — тривиальный <c>GET</c> по фиксированному пути без
/// параметров и аргументов, поэтому их скелет идентичен и выделен сюда.
/// </summary>
internal static class RefCommandFactory
{
    /// <summary>
    /// Строит справочную subcommand с заданным именем, описанием и API-путём.
    /// </summary>
    /// <param name="name">Имя подкоманды (например, <c>statuses</c>).</param>
    /// <param name="description">Help-описание подкоманды.</param>
    /// <param name="path">Относительный путь API (без лидирующего слэша), например <c>statuses</c>.</param>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build(string name, string description, string path)
    {
        var cmd = new Command(name, description);
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

                var result = await ctx.Client.GetAsync(path, ct);
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
