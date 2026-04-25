namespace YandexTrackerCLI.Commands.Queue;

using System.CommandLine;
using System.Text.Json;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt queue list</c>: выполняет <c>GET /v3/queues</c> с пагинацией
/// через <see cref="YandexTrackerCLI.Core.Api.TrackerClient.GetPagedAsync"/> и печатает
/// все элементы как единый JSON-массив на stdout. Лимит записей задаётся через <c>--max</c>.
/// </summary>
public static class QueueListCommand
{
    /// <summary>
    /// Строит subcommand <c>list</c> для <c>yt queue</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var maxOption = new Option<int>("--max")
        {
            Description = "Лимит записей (default 10000).",
            DefaultValueFactory = _ => 10_000,
        };

        var cmd = new Command("list", "Список очередей с пагинацией.");
        cmd.Options.Add(maxOption);

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

                var max = parseResult.GetValue(maxOption);

                using var ms = new MemoryStream();
                await using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
                {
                    w.WriteStartArray();
                    var count = 0;
                    await foreach (var el in ctx.Client.GetPagedAsync("queues", ct: ct))
                    {
                        el.WriteTo(w);
                        if (++count >= max)
                        {
                            break;
                        }
                    }
                    w.WriteEndArray();
                }

                using var doc = JsonDocument.Parse(ms.ToArray());
                JsonWriter.Write(
                    Console.Out,
                    doc.RootElement,
                    ctx.EffectiveOutputFormat,
                    pretty: !Console.IsOutputRedirected);
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
