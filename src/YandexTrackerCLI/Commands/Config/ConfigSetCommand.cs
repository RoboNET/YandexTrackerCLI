namespace YandexTrackerCLI.Commands.Config;

using System.CommandLine;
using Core.Api.Errors;
using YandexTrackerCLI.Core.Config;
using Output;

/// <summary>
/// Команда <c>yt config set &lt;key&gt; &lt;value&gt;</c>: записывает значение по dotted-path ключу
/// из allowlist и сохраняет файл конфигурации.
/// </summary>
/// <remarks>
/// Политики профиля через эту команду можно только <b>ужесточить</b> (см.
/// <see cref="ConfigPolicyGuard"/>): снятие <c>read_only</c> и снятие/расширение
/// <c>allowed_queues</c>/<c>allowed_write_issues</c> отклоняются с
/// <see cref="ErrorCode.PolicyViolation"/> (exit 10).
/// </remarks>
public static class ConfigSetCommand
{
    /// <summary>
    /// Строит subcommand <c>set</c> для <c>yt config</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("key")
        {
            Description = "Ключ вида org_id, auth.token, ...",
        };
        var valueArg = new Argument<string>("value")
        {
            Description = "Значение для записи.",
        };

        var cmd = new Command("set", "Записать значение в конфиг (только разрешённые ключи).");
        cmd.Arguments.Add(keyArg);
        cmd.Arguments.Add(valueArg);

        cmd.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var key = parseResult.GetValue(keyArg)!;
                var value = parseResult.GetValue(valueArg)!;
                ConfigKeyAccess.EnsureAllowed(key);

                var store = new ConfigStore(ConfigStore.DefaultPath);
                var cfg = await store.LoadAsync(ct);
                var name = parseResult.GetValue(RootCommandBuilder.ProfileOption) ?? cfg.DefaultProfile;

                if (!cfg.Profiles.TryGetValue(name, out var profile))
                {
                    throw new TrackerException(ErrorCode.ConfigError, $"Profile '{name}' not found.");
                }

                var updated = ConfigKeyAccess.WriteValue(profile, key, value);
                // Политики профиля можно только ужесточить: снятие идёт через пересоздание
                // профиля (`yt auth login`), а не через ту же команду, которой располагает
                // ограничиваемый вызывающий.
                ConfigPolicyGuard.EnsureNotWeakened(profile, updated, name);
                var profiles = new Dictionary<string, Profile>(cfg.Profiles) { [name] = updated };
                await store.SaveAsync(new ConfigFile(cfg.DefaultProfile, profiles), ct);

                CommandOutput.WriteSingleField("updated", key);
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
