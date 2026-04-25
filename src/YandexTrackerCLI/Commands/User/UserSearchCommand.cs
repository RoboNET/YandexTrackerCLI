namespace YandexTrackerCLI.Commands.User;

using System.CommandLine;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt user search --query &lt;text&gt;</c>: выполняет
/// <c>GET /v3/users?query={text}</c> и печатает ответ сервера как есть
/// (API возвращает JSON-массив пользователей). Эндпоинт не поддерживает
/// пагинацию — ответ целиком помещается в stdout.
/// </summary>
public static class UserSearchCommand
{
    /// <summary>
    /// Строит subcommand <c>search</c> для <c>yt user</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var queryOpt = new Option<string>("--query")
        {
            Description = "Текст поиска (обязательно).",
            Required = true,
        };

        var cmd = new Command("search", "Поиск пользователей по тексту (GET /v3/users?query=...).");
        cmd.Options.Add(queryOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var query = pr.GetValue(queryOpt)!;
                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.GetAsync(
                    $"users?query={Uri.EscapeDataString(query)}",
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
