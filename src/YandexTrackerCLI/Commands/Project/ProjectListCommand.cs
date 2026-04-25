namespace YandexTrackerCLI.Commands.Project;

using System.CommandLine;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Core.Api;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt project list [--json-file &lt;filter.json&gt;] [--json-stdin] [--max N]</c>:
/// выполняет <c>POST /v3/entities/project/_search</c> с опциональным фильтром в теле запроса
/// (по умолчанию — пустой объект <c>{}</c>) и постраничным обходом через
/// <see cref="TrackerClient.PostJsonRawWithHeadersAsync"/>, склеивая все страницы в
/// единый JSON-массив на stdout. Эндпоинт <c>_search</c> считается read-only (см.
/// <see cref="YandexTrackerCLI.Core.Http.ReadOnlyGuardHandler"/>), поэтому команда разрешена
/// в read-only профиле.
/// </summary>
public static class ProjectListCommand
{
    /// <summary>
    /// Строит subcommand <c>list</c> для <c>yt project</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var jsonFileOpt = new Option<string?>("--json-file")
        {
            Description = "Путь к JSON-файлу с телом-фильтром для _search.",
        };
        var jsonStdinOpt = new Option<bool>("--json-stdin")
        {
            Description = "Читать JSON-тело-фильтр из stdin.",
        };
        var maxOpt = new Option<int>("--max")
        {
            Description = "Лимит записей (default 10000).",
            DefaultValueFactory = _ => 10_000,
        };
        var perPageOpt = new Option<int>("--per-page")
        {
            Description = "Размер страницы (default 50).",
            DefaultValueFactory = _ => 50,
        };

        var cmd = new Command("list", "Список проектов (POST /v3/entities/project/_search).");
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);
        cmd.Options.Add(maxOpt);
        cmd.Options.Add(perPageOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);
                var max = pr.GetValue(maxOpt);
                var perPage = pr.GetValue(perPageOpt);

                var body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In) ?? "{}";

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                await WriteJsonArray(ctx.Client, body, perPage, max, ct);
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
    /// Склеивает все страницы ответа в единый JSON-массив на stdout.
    /// </summary>
    /// <param name="client">HTTP-клиент Tracker API.</param>
    /// <param name="bodyJson">Тело запроса (фильтр для <c>_search</c>).</param>
    /// <param name="perPage">Размер страницы.</param>
    /// <param name="max">Максимум записей.</param>
    /// <param name="ct">Токен отмены.</param>
    private static async Task WriteJsonArray(
        TrackerClient client,
        string bodyJson,
        int perPage,
        int max,
        CancellationToken ct)
    {
        var pretty = !Console.IsOutputRedirected;
        using var ms = new MemoryStream();
        await using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = pretty }))
        {
            w.WriteStartArray();
            var count = 0;
            await foreach (var el in SearchPaged(client, bodyJson, perPage, max, ct))
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
        if (pretty)
        {
            await Console.Out.WriteLineAsync();
        }
    }

    /// <summary>
    /// Итерирует <c>POST /entities/project/_search</c> постранично, используя
    /// <see cref="TrackerClient.PostJsonRawWithHeadersAsync"/> и заголовок
    /// <c>X-Total-Pages</c> для остановки.
    /// </summary>
    /// <param name="client">HTTP-клиент Tracker API.</param>
    /// <param name="bodyJson">Тело запроса (фильтр для <c>_search</c>).</param>
    /// <param name="perPage">Размер страницы.</param>
    /// <param name="max">Максимум записей.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Асинхронный поток JSON-элементов ответа.</returns>
    private static async IAsyncEnumerable<JsonElement> SearchPaged(
        TrackerClient client,
        string bodyJson,
        int perPage,
        int max,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var page = 1;
        var emitted = 0;
        while (true)
        {
            var path = $"entities/project/_search?perPage={perPage}&page={page}";
            var (payload, totalPages) = await client.PostJsonRawWithHeadersAsync(path, bodyJson, ct);

            if (payload.ValueKind != JsonValueKind.Array)
            {
                yield return payload;
                yield break;
            }

            foreach (var item in payload.EnumerateArray())
            {
                yield return item;
                emitted++;
                if (emitted >= max)
                {
                    yield break;
                }
            }

            if (page >= totalPages)
            {
                yield break;
            }

            page++;
        }
    }
}
