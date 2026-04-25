namespace YandexTrackerCLI.Commands.Config;

using System.CommandLine;
using System.Text.Json;
using YandexTrackerCLI.Core.Config;
using Output;

/// <summary>
/// Команда <c>yt config list</c>: печатает JSON-массив имён профилей из конфига.
/// </summary>
public static class ConfigListCommand
{
    /// <summary>
    /// Строит subcommand <c>list</c> для <c>yt config</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var cmd = new Command("list", "Список профилей.");
        cmd.SetAction(async (parseResult, ct) =>
        {
            var cfg = await new ConfigStore(ConfigStore.DefaultPath).LoadAsync(ct);

            using var ms = new MemoryStream();
            await using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
            {
                w.WriteStartArray();
                foreach (var name in cfg.Profiles.Keys.OrderBy(x => x))
                {
                    w.WriteStringValue(name);
                }
                w.WriteEndArray();
            }

            // ConfigListCommand не работает с профилем (это сама команда листинга профилей),
            // поэтому передаём profileDefaultFormat=null. Cascade в FormatResolver сам
            // подберёт формат из CLI/env/TTY.
            using var doc = JsonDocument.Parse(ms.ToArray());
            var format = CommandFormatHelper.ResolveForCommand(parseResult, profileDefaultFormat: null);
            JsonWriter.Write(
                Console.Out,
                doc.RootElement,
                format,
                pretty: CommandFormatHelper.ResolvePretty());

            return 0;
        });
        return cmd;
    }
}
