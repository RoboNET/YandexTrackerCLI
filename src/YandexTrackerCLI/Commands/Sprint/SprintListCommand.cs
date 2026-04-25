namespace YandexTrackerCLI.Commands.Sprint;

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt sprint list [--board &lt;id&gt;]</c>: выполняет <c>GET /v3/sprints</c>
/// (без фильтра) либо <c>GET /v3/boards/{id}/sprints</c> (при заданном <c>--board</c>)
/// с пагинацией через <see cref="YandexTrackerCLI.Core.Api.TrackerClient.GetPagedAsync"/>
/// и печатает все элементы как единый JSON-массив на stdout.
/// Лимит записей задаётся через <c>--max</c>.
/// </summary>
public static class SprintListCommand
{
    /// <summary>
    /// Строит subcommand <c>list</c> для <c>yt sprint</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var boardOption = new Option<string?>("--board")
        {
            Description = "Идентификатор доски; если задан, запрашивается /boards/{id}/sprints.",
        };

        var maxOption = new Option<int>("--max")
        {
            Description = "Лимит записей (default 10000).",
            DefaultValueFactory = _ => 10_000,
        };

        var cmd = new Command("list", "Список спринтов с пагинацией.");
        cmd.Options.Add(boardOption);
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
                var board = parseResult.GetValue(boardOption);
                var path = string.IsNullOrWhiteSpace(board)
                    ? "sprints"
                    : $"boards/{Uri.EscapeDataString(board)}/sprints";

                using var ms = new MemoryStream();
                await using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = !Console.IsOutputRedirected }))
                {
                    w.WriteStartArray();
                    var count = 0;
                    await foreach (var el in ctx.Client.GetPagedAsync(path, ct: ct))
                    {
                        el.WriteTo(w);
                        if (++count >= max)
                        {
                            break;
                        }
                    }
                    w.WriteEndArray();
                }

                await Console.Out.WriteAsync(Encoding.UTF8.GetString(ms.ToArray()));
                if (!Console.IsOutputRedirected)
                {
                    await Console.Out.WriteLineAsync();
                }
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
