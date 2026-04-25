namespace YandexTrackerCLI.Commands.Issue;

using System.CommandLine;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Core.Api;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt issue find --yql "..."</c>: выполняет <c>POST /v3/issues/_search</c>
/// с постраничным обходом и выводит результат либо как склеенный JSON-массив (по умолчанию),
/// либо построчно в NDJSON при включённом <c>--stream</c>.
/// </summary>
public static class IssueFindCommand
{
    /// <summary>
    /// Строит subcommand <c>find</c> для <c>yt issue</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var yqlOpt = new Option<string?>("--yql")
        {
            Description = "YQL-запрос; взаимоисключается с simple-фильтрами (--queue/--status/...).",
        };
        var queueOpt = new Option<string?>("--queue")
        {
            Description = "Фильтр по очереди, например DEV.",
        };
        var statusOpt = new Option<string?>("--status")
        {
            Description = "Статус или список статусов через запятую (open,in-progress).",
        };
        var assigneeOpt = new Option<string?>("--assignee")
        {
            Description = "Исполнитель: логин или специальное значение 'me'.",
        };
        var typeOpt = new Option<string?>("--type")
        {
            Description = "Тип или список типов через запятую (bug,task).",
        };
        var priorityOpt = new Option<string?>("--priority")
        {
            Description = "Приоритет, например minor/normal/critical.",
        };
        var updatedSinceOpt = new Option<string?>("--updated-since")
        {
            Description = "Нижняя граница поля Updated (ISO-8601, например 2024-01-01).",
        };
        var createdSinceOpt = new Option<string?>("--created-since")
        {
            Description = "Нижняя граница поля Created (ISO-8601, например 2024-01-01).",
        };
        var textOpt = new Option<string?>("--text")
        {
            Description = "Полнотекстовый поиск по Summary или Description.",
        };
        var tagOpt = new Option<string?>("--tag")
        {
            Description = "Тег или список тегов через запятую (release,regression).",
        };
        var maxOpt = new Option<int>("--max")
        {
            Description = "Максимум записей (default 10000).",
            DefaultValueFactory = _ => 10_000,
        };
        var perPageOpt = new Option<int>("--per-page")
        {
            Description = "Размер страницы (default 50).",
            DefaultValueFactory = _ => 50,
        };
        var streamOpt = new Option<bool>("--stream")
        {
            Description = "NDJSON: по одному JSON-объекту на строку.",
        };

        var cmd = new Command("find", "Поиск задач по YQL (POST /v3/issues/_search).");
        cmd.Options.Add(yqlOpt);
        cmd.Options.Add(queueOpt);
        cmd.Options.Add(statusOpt);
        cmd.Options.Add(assigneeOpt);
        cmd.Options.Add(typeOpt);
        cmd.Options.Add(priorityOpt);
        cmd.Options.Add(updatedSinceOpt);
        cmd.Options.Add(createdSinceOpt);
        cmd.Options.Add(textOpt);
        cmd.Options.Add(tagOpt);
        cmd.Options.Add(maxOpt);
        cmd.Options.Add(perPageOpt);
        cmd.Options.Add(streamOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var filters = new IssueFilters(
                    Yql: pr.GetValue(yqlOpt),
                    Queue: pr.GetValue(queueOpt),
                    Status: pr.GetValue(statusOpt),
                    Assignee: pr.GetValue(assigneeOpt),
                    Type: pr.GetValue(typeOpt),
                    Priority: pr.GetValue(priorityOpt),
                    UpdatedSince: pr.GetValue(updatedSinceOpt),
                    CreatedSince: pr.GetValue(createdSinceOpt),
                    Text: pr.GetValue(textOpt),
                    Tag: pr.GetValue(tagOpt));
                var yql = IssueFilterBuilder.Build(filters);
                var max = pr.GetValue(maxOpt);
                var perPage = pr.GetValue(perPageOpt);
                var stream = pr.GetValue(streamOpt);

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var bodyJson = BuildBody(yql);

                if (stream)
                {
                    await StreamNdjsonAsync(ctx.Client, bodyJson, perPage, max, ct);
                }
                else
                {
                    await WriteAggregatedAsync(ctx.Client, bodyJson, perPage, max, ctx.EffectiveOutputFormat, ct);
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
    /// Сериализует минимальное тело запроса <c>{"query":"..."}</c> через <see cref="Utf8JsonWriter"/>,
    /// избегая зависимости от внутреннего <c>TrackerJsonContext</c> из другого сборки.
    /// </summary>
    /// <param name="yql">YQL-выражение.</param>
    /// <returns>UTF-8 строка тела запроса.</returns>
    private static string BuildBody(string yql)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("query", yql);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Собирает все страницы результата в единый JSON-массив, затем рендерит через
    /// <see cref="JsonWriter.Write"/> в указанном формате (json/minimal/table).
    /// Ограничивается <paramref name="max"/> записями.
    /// </summary>
    private static async Task WriteAggregatedAsync(
        TrackerClient client,
        string bodyJson,
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
            var count = 0;
            await foreach (var el in SearchPagedAsync(client, bodyJson, perPage, max, ct))
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
        JsonWriter.Write(Console.Out, doc.RootElement, format, pretty);
    }

    /// <summary>
    /// Печатает каждый элемент как отдельную строку NDJSON (без отступов).
    /// </summary>
    private static async Task StreamNdjsonAsync(
        TrackerClient client,
        string bodyJson,
        int perPage,
        int max,
        CancellationToken ct)
    {
        var count = 0;
        await foreach (var el in SearchPagedAsync(client, bodyJson, perPage, max, ct))
        {
            using var ms = new MemoryStream();
            await using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
            {
                el.WriteTo(w);
            }

            await Console.Out.WriteLineAsync(Encoding.UTF8.GetString(ms.ToArray()));
            if (++count >= max)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Итерирует результат <c>POST /issues/_search</c> постранично через
    /// <see cref="TrackerClient.PostJsonRawWithHeadersAsync"/>, читая
    /// заголовок <c>X-Total-Pages</c> для остановки.
    /// </summary>
    private static async IAsyncEnumerable<JsonElement> SearchPagedAsync(
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
            var path = $"issues/_search?perPage={perPage}&page={page}";
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
