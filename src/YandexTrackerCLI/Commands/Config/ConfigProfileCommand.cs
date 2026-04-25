namespace YandexTrackerCLI.Commands.Config;

using System.CommandLine;
using Core.Api.Errors;
using YandexTrackerCLI.Core.Config;
using Output;

/// <summary>
/// Команда <c>yt config profile &lt;name&gt;</c>: меняет <c>default_profile</c> в файле конфигурации.
/// </summary>
public static class ConfigProfileCommand
{
    /// <summary>
    /// Строит subcommand <c>profile</c> для <c>yt config</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var nameArg = new Argument<string>("name")
        {
            Description = "Имя профиля, который станет default.",
        };

        var cmd = new Command("profile", "Переключить default профиль.");
        cmd.Arguments.Add(nameArg);

        cmd.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var name = parseResult.GetValue(nameArg)!;
                var store = new ConfigStore(ConfigStore.DefaultPath);
                var cfg = await store.LoadAsync(ct);

                if (!cfg.Profiles.ContainsKey(name))
                {
                    throw new TrackerException(ErrorCode.ConfigError, $"Profile '{name}' not found.");
                }

                await store.SaveAsync(new ConfigFile(name, cfg.Profiles), ct);

                CommandOutput.WriteSingleField("default_profile", name);
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
