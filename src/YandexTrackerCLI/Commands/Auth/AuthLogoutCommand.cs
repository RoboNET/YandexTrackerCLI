namespace YandexTrackerCLI.Commands.Auth;

using System.CommandLine;
using Core.Api.Errors;
using YandexTrackerCLI.Core.Config;
using Output;

/// <summary>
/// Команда <c>yt auth logout</c>: удаляет токен и inline PEM из выбранного профиля,
/// оставляя метаданные организации (<c>org_type</c>/<c>org_id</c>/<c>read_only</c>) и
/// идентификаторы сервис-аккаунта (<c>service_account_id</c>/<c>key_id</c>/<c>private_key_path</c>).
/// </summary>
public static class AuthLogoutCommand
{
    /// <summary>
    /// Строит subcommand <c>logout</c> для <c>yt auth</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var cmd = new Command("logout", "Удалить токен профиля (метаданные org/service-account сохраняются).");
        cmd.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var store = new ConfigStore(ConfigStore.DefaultPath);
                var cfg = await store.LoadAsync(ct);
                var name = parseResult.GetValue(RootCommandBuilder.ProfileOption) ?? cfg.DefaultProfile;

                if (!cfg.Profiles.TryGetValue(name, out var existing))
                {
                    throw new TrackerException(ErrorCode.ConfigError, $"Profile '{name}' not found.");
                }

                // Оставляем type/sa/key_id/path и default_format, чистим Token и PrivateKeyPem.
                var cleared = existing with
                {
                    Auth = new AuthConfig(
                        existing.Auth.Type,
                        Token: null,
                        ServiceAccountId: existing.Auth.ServiceAccountId,
                        KeyId: existing.Auth.KeyId,
                        PrivateKeyPath: existing.Auth.PrivateKeyPath,
                        PrivateKeyPem: null),
                };

                var profiles = new Dictionary<string, Profile>(cfg.Profiles) { [name] = cleared };
                await store.SaveAsync(new ConfigFile(cfg.DefaultProfile, profiles), ct);

                CommandOutput.WriteSingleField("logged_out", name);
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
