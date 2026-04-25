namespace YandexTrackerCLI.Commands.Auth;

using System.CommandLine;
using System.Text.Json;
using Core.Api.Errors;
using YandexTrackerCLI.Core.Config;
using Output;

/// <summary>
/// Команда <c>yt auth status</c>: печатает JSON с активным профилем,
/// типом организации и типом аутентификации.
/// </summary>
public static class AuthStatusCommand
{
    /// <summary>
    /// Строит subcommand <c>status</c> для <c>yt auth</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var cmd = new Command("status", "Показывает активный профиль и тип аутентификации.");
        cmd.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var profileName = parseResult.GetValue(RootCommandBuilder.ProfileOption);
                var readOnly = parseResult.GetValue(RootCommandBuilder.ReadOnlyOption);

                var store = new ConfigStore(ConfigStore.DefaultPath);
                var cfg = await store.LoadAsync(ct);
                var env = EnvReader.Snapshot();
                var eff = EnvOverrides.Resolve(cfg, profileName, env, readOnly);

                using var ms = new MemoryStream();
                await using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
                {
                    w.WriteStartObject();
                    w.WriteString("profile", eff.Name);
                    w.WriteString("org_type", eff.OrgType == OrgType.Cloud ? "cloud" : "yandex360");
                    w.WriteString("org_id", eff.OrgId);
                    w.WriteString("auth_type", eff.Auth.Type switch
                    {
                        AuthType.OAuth => "oauth",
                        AuthType.IamStatic => "iam-static",
                        AuthType.ServiceAccount => "service-account",
                        _ => "unknown",
                    });
                    w.WriteBoolean("read_only", eff.ReadOnly);
                    w.WriteEndObject();
                }

                using var doc = JsonDocument.Parse(ms.ToArray());
                var format = CommandFormatHelper.ResolveForCommand(parseResult, eff.DefaultFormat);
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
