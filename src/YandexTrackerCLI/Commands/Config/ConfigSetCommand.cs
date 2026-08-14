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
                var explicitProfile = parseResult.GetValue(RootCommandBuilder.ProfileOption);

                await store.ModifyAsync(
                    fresh =>
                    {
                        // Профиль резолвится по тому же снимку, который будет записан: имя,
                        // взятое из одного чтения и применённое к другому, снова открывало бы
                        // окно гонки с `yt config profile`.
                        var target = explicitProfile ?? fresh.DefaultProfile;
                        if (!fresh.Profiles.TryGetValue(target, out var profile))
                        {
                            throw new TrackerException(ErrorCode.ConfigError, $"Profile '{target}' not found.");
                        }

                        var updated = ConfigKeyAccess.WriteValue(profile, key, value);
                        // Политики профиля можно только ужесточить: снятие идёт через пересоздание
                        // профиля (`yt auth login`), а не через ту же команду, которой располагает
                        // ограничиваемый вызывающий. Сравнение идёт со свежим снимком с диска —
                        // иначе ужесточение, пришедшее из параллельного запуска, было бы
                        // невидимо для проверки и молча откатилось бы этой записью.
                        ConfigPolicyGuard.EnsureNotWeakened(profile, updated, target);
                        var profiles = new Dictionary<string, Profile>(fresh.Profiles) { [target] = updated };
                        return new ConfigFile(fresh.DefaultProfile, profiles);
                    },
                    ct);

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
