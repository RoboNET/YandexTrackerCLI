namespace YandexTrackerCLI.Commands.Config;

using System.CommandLine;
using System.Text.Json;
using Core.Api.Errors;
using YandexTrackerCLI.Core.Config;
using Output;

/// <summary>
/// Команда <c>yt config get &lt;key&gt;</c>: читает значение по dotted-path ключу из allowlist.
/// </summary>
/// <remarks>
/// Значения секретных ключей (<c>auth.token</c>, <c>auth.private_key_pem</c>) маскируются как <c>"***"</c>.
/// Результат сериализуется как JSON-строка.
/// </remarks>
public static class ConfigGetCommand
{
    /// <summary>
    /// Строит subcommand <c>get</c> для <c>yt config</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("key")
        {
            Description = "Ключ вида org_id, auth.token, ...",
        };

        var cmd = new Command(
            "get",
            "Прочитать значение конфигурации (auth.token и private_key_pem маскируются как \"***\").");
        cmd.Arguments.Add(keyArg);

        cmd.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var key = parseResult.GetValue(keyArg)!;
                ConfigKeyAccess.EnsureAllowed(key);

                var store = new ConfigStore(ConfigStore.DefaultPath);
                var cfg = await store.LoadAsync(ct);
                var name = parseResult.GetValue(RootCommandBuilder.ProfileOption) ?? cfg.DefaultProfile;

                if (!cfg.Profiles.TryGetValue(name, out var profile))
                {
                    throw new TrackerException(ErrorCode.ConfigError, $"Profile '{name}' not found.");
                }

                var raw = ConfigKeyAccess.ReadValue(profile, key);
                var display = ConfigKeyAccess.IsSecret(key) && !string.IsNullOrEmpty(raw) ? "***" : raw;

                using var ms = new MemoryStream();
                await using (var w = new Utf8JsonWriter(ms))
                {
                    if (display is null)
                    {
                        w.WriteNullValue();
                    }
                    else
                    {
                        w.WriteStringValue(display);
                    }
                }

                using var doc = JsonDocument.Parse(ms.ToArray());
                var format = CommandFormatHelper.ResolveForCommand(parseResult, profile.DefaultFormat);
                JsonWriter.Write(
                    Console.Out,
                    doc.RootElement,
                    format,
                    pretty: CommandFormatHelper.ResolvePretty());

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
