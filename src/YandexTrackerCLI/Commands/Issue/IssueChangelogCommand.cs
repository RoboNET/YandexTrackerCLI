namespace YandexTrackerCLI.Commands.Issue;

using System.CommandLine;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Core.Api;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt issue changelog &lt;key&gt;</c>: выгрузка истории изменений задачи
/// (<c>GET /v3/issues/{key}/changelog</c>) с постраничным обходом. Полезна для анализа
/// времени по статусам (cycle time). Результат выводится либо как склеенный JSON-массив
/// (по умолчанию), либо построчно в NDJSON при включённом <c>--stream</c>.
/// </summary>
/// <remarks>
/// Это read-only (GET), совместимо с <c>--read-only</c>.
/// <para>
/// Пагинация: changelog у Tracker использует курсорную пагинацию через заголовок <c>Link</c>
/// с <c>rel="next"</c> (а не <c>page</c>/<c>X-Total-Pages</c>). Обход выполняется через
/// <see cref="TrackerClient.GetCursorPagedAsync"/>, который следует за курсором до конца истории,
/// поэтому полная история выгружается независимо от <c>--per-page</c> и ограничивается только
/// флагом <c>--max</c>. Пустой changelog даёт <c>[]</c> и exit 0.
/// </para>
/// </remarks>
public static class IssueChangelogCommand
{
    /// <summary>
    /// Строит subcommand <c>changelog</c> для <c>yt issue</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("key") { Description = "Ключ задачи (например DEV-1)." };

        var perPageOpt = new Option<int>("--per-page")
        {
            Description = "Размер страницы (default 100).",
            DefaultValueFactory = _ => 100,
        };
        var maxOpt = new Option<int>("--max")
        {
            Description = "Максимум записей (default 10000).",
            DefaultValueFactory = _ => 10_000,
        };
        var streamOpt = new Option<bool>("--stream")
        {
            Description = "NDJSON: по одному JSON-объекту на строку.",
        };

        var cmd = new Command("changelog", "История изменений задачи (GET /v3/issues/{key}/changelog).");
        cmd.Arguments.Add(keyArg);
        cmd.Options.Add(perPageOpt);
        cmd.Options.Add(maxOpt);
        cmd.Options.Add(streamOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                var perPage = pr.GetValue(perPageOpt);
                var max = pr.GetValue(maxOpt);
                var stream = pr.GetValue(streamOpt);

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var path = $"issues/{Uri.EscapeDataString(key)}/changelog";

                if (stream)
                {
                    await StreamNdjsonAsync(ctx.Client, path, perPage, max, ct);
                }
                else
                {
                    await WriteAggregatedAsync(ctx.Client, path, perPage, max, ctx.EffectiveOutputFormat, ct);
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

    /// <summary>
    /// Собирает все страницы истории в единый JSON-массив, затем рендерит через
    /// <see cref="JsonWriter.Write"/> в указанном формате (json/minimal/table).
    /// Ограничивается <paramref name="max"/> записями.
    /// </summary>
    private static async Task WriteAggregatedAsync(
        TrackerClient client,
        string path,
        int perPage,
        int max,
        OutputFormat format,
        CancellationToken ct)
    {
        var pretty = !Console.IsOutputRedirected;
        using var ms = new MemoryStream();
        await using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartArray();
            await foreach (var el in TakeAsync(client.GetCursorPagedAsync(path, perPage, ct), max, ct))
            {
                el.WriteTo(w);
            }

            w.WriteEndArray();
        }

        using var doc = JsonDocument.Parse(ms.ToArray());
        JsonWriter.Write(Console.Out, doc.RootElement, format, pretty);
    }

    /// <summary>
    /// Печатает каждый элемент истории как отдельную строку NDJSON (без отступов).
    /// Ограничивается <paramref name="max"/> записями.
    /// </summary>
    private static async Task StreamNdjsonAsync(
        TrackerClient client,
        string path,
        int perPage,
        int max,
        CancellationToken ct)
    {
        await foreach (var el in TakeAsync(client.GetCursorPagedAsync(path, perPage, ct), max, ct))
        {
            using var ms = new MemoryStream();
            await using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
            {
                el.WriteTo(w);
            }

            await Console.Out.WriteLineAsync(Encoding.UTF8.GetString(ms.ToArray()));
        }
    }

    /// <summary>
    /// Ограничивает асинхронную последовательность первыми <paramref name="max"/> элементами,
    /// пропуская «пустые» элементы (<see cref="JsonValueKind.Undefined"/>), которые возникают при
    /// пустом теле ответа (204 / Content-Length 0) — иначе <see cref="JsonElement.WriteTo"/> бросает.
    /// </summary>
    private static async IAsyncEnumerable<JsonElement> TakeAsync(
        IAsyncEnumerable<JsonElement> source,
        int max,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var count = 0;
        await foreach (var el in source.WithCancellation(ct))
        {
            if (el.ValueKind == JsonValueKind.Undefined)
            {
                continue;
            }

            if (count >= max)
            {
                yield break;
            }

            yield return el;
            count++;
        }
    }
}
