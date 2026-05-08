namespace YandexTrackerCLI.Commands.Automation.Autoaction;

using System.CommandLine;
using System.Text.Json;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt automation autoaction list</c>: выполняет
/// <c>GET /v3/queues/{queue}/autoactions/</c> с пагинацией через
/// <see cref="YandexTrackerCLI.Core.Api.TrackerClient.GetPagedAsync"/>
/// и печатает список автодействий очереди как единый JSON-массив на stdout.
/// Лимит записей задаётся через <c>--max</c>.
/// </summary>
public static class AutoactionListCommand
{
    /// <summary>
    /// Строит subcommand <c>list</c> для группы <c>yt automation autoaction</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var queueOption = new Option<string>("--queue")
        {
            Description = "Ключ очереди.",
            Required = true,
        };
        var maxOption = new Option<int>("--max")
        {
            Description = "Лимит записей (default 10000).",
            DefaultValueFactory = _ => 10_000,
        };

        var cmd = new Command("list", "Список автодействий очереди.");
        cmd.Options.Add(queueOption);
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

                var queue = parseResult.GetValue(queueOption)!;
                var max = parseResult.GetValue(maxOption);

                using var ms = new MemoryStream();
                await using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
                {
                    w.WriteStartArray();
                    var count = 0;
                    await foreach (var el in ctx.Client.GetPagedAsync(
                        $"queues/{Uri.EscapeDataString(queue)}/autoactions/",
                        ct: ct))
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
