namespace YandexTrackerCLI.Commands.Worklog;

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt worklog list &lt;issue-key&gt;</c>: выполняет <c>GET /v3/issues/{key}/worklog</c>
/// с пагинацией через <see cref="YandexTrackerCLI.Core.Api.TrackerClient.GetPagedAsync"/>
/// и печатает все элементы как единый JSON-массив на stdout.
/// Лимит записей задаётся через <c>--max</c>.
/// </summary>
public static class WorklogListCommand
{
    /// <summary>
    /// Строит subcommand <c>list</c> для <c>yt worklog</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("issue-key") { Description = "Ключ задачи (например DEV-1)." };
        var maxOpt = new Option<int>("--max")
        {
            Description = "Лимит записей (default 10000).",
            DefaultValueFactory = _ => 10_000,
        };

        var cmd = new Command("list", "Список записей учёта времени задачи (GET /v3/issues/{key}/worklog).");
        cmd.Arguments.Add(keyArg);
        cmd.Options.Add(maxOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);
                var key = pr.GetValue(keyArg)!;
                var max = pr.GetValue(maxOpt);

                using var ms = new MemoryStream();
                await using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = !Console.IsOutputRedirected }))
                {
                    w.WriteStartArray();
                    var count = 0;
                    await foreach (var el in ctx.Client.GetPagedAsync(
                        $"issues/{Uri.EscapeDataString(key)}/worklog",
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
