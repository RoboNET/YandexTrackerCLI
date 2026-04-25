namespace YandexTrackerCLI.Commands.Comment;

using System.CommandLine;
using System.Text.Json;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt comment list &lt;issue-key&gt;</c>: выполняет <c>GET /v3/issues/{key}/comments</c>
/// с пагинацией через <see cref="YandexTrackerCLI.Core.Api.TrackerClient.GetPagedAsync"/>
/// и печатает результат.
/// </summary>
/// <remarks>
/// Для форматов <see cref="OutputFormat.Json"/> и <see cref="OutputFormat.Minimal"/> и для
/// перенаправленного stdout — единый JSON-массив. Для <see cref="OutputFormat.Table"/> в TTY —
/// rich block-view через <see cref="CommentBlockRenderer"/> с markdown-разметкой и
/// опциональным pager'ом.
/// </remarks>
public static class CommentListCommand
{
    /// <summary>
    /// Строит subcommand <c>list</c> для <c>yt comment</c>.
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

        var cmd = new Command("list", "Список комментариев задачи (GET /v3/issues/{key}/comments).");
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
                    cliNoColor: pr.GetValue(RootCommandBuilder.NoColorOption),
                    cliNoPager: pr.GetValue(RootCommandBuilder.NoPagerOption),
                    ct: ct);
                var key = pr.GetValue(keyArg)!;
                var max = pr.GetValue(maxOpt);

                // Aggregate comments into a JSON array first; this lets table-format render
                // a rich block-view, and other formats stream the array as before.
                using var ms = new MemoryStream();
                await using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
                {
                    w.WriteStartArray();
                    var count = 0;
                    await foreach (var el in ctx.Client.GetPagedAsync(
                        $"issues/{Uri.EscapeDataString(key)}/comments",
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

                if (ctx.EffectiveOutputFormat == OutputFormat.Table)
                {
                    using var doc = JsonDocument.Parse(ms.ToArray());
                    using var pager = PagerWriter.Create(ctx.TerminalCapabilities, Console.Out);
                    CommentBlockRenderer.Render(pager, doc.RootElement, ctx.TerminalCapabilities);
                    pager.Flush();
                }
                else
                {
                    using var doc = JsonDocument.Parse(ms.ToArray());
                    JsonWriter.Write(
                        Console.Out,
                        doc.RootElement,
                        ctx.EffectiveOutputFormat,
                        pretty: !Console.IsOutputRedirected);
                    if (!Console.IsOutputRedirected
                        && ctx.EffectiveOutputFormat == OutputFormat.Json)
                    {
                        // JsonWriter уже добавляет trailing newline в pretty режиме — поэтому
                        // здесь нечего делать. Этот блок оставлен для самодокументирования.
                    }
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
