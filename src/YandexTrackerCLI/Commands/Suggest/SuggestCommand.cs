namespace YandexTrackerCLI.Commands.Suggest;

using System.Collections.Concurrent;
using System.CommandLine;
using System.Linq;
using System.Text.Json;
using Core.Api;
using Core.Api.Errors;
using Output;
using Spectre.Console;
using Spectre.Console.Rendering;

/// <summary>
/// Команда <c>yt suggest [input]</c> — интерактивный TTY-only fuzzy-поиск задач
/// через <c>GET /v3/issues/_suggest</c>. Печатает выбранный ключ задачи в stdout
/// (композируется с другими командами: <c>yt issue get $(yt suggest)</c>).
/// </summary>
/// <remarks>
/// Требует TTY на stdin и stdout. При перенаправлении возвращает
/// <see cref="ErrorCode.InvalidArgs"/>. При отмене (Esc) возвращает 130.
/// </remarks>
public static class SuggestCommand
{
    private const int DebounceMilliseconds = 250;

    /// <summary>
    /// Строит top-level subcommand <c>suggest</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var inputArg = new Argument<string?>("input")
        {
            Description = "Начальный текст поиска (опционально).",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var queueOpt = new Option<string?>("--queue")
        {
            Description = "Ограничить очередью (queue query-параметр API).",
        };
        var limitOpt = new Option<int>("--limit")
        {
            Description = "Сколько результатов показывать (default 10, API cap ~20).",
            DefaultValueFactory = _ => 10,
        };

        var cmd = new Command("suggest", "Интерактивный fuzzy-поиск задач (TTY only). Печатает выбранный key.");
        cmd.Arguments.Add(inputArg);
        cmd.Options.Add(queueOpt);
        cmd.Options.Add(limitOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            try
            {
                if (Console.IsInputRedirected || Console.IsOutputRedirected)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "yt suggest требует TTY. Для скриптов используйте 'yt issue find'.");
                }

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: parseResult.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: parseResult.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: parseResult.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: parseResult.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !parseResult.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: parseResult.GetValue(RootCommandBuilder.FormatOption),
                    cliNoColor: parseResult.GetValue(RootCommandBuilder.NoColorOption),
                    cliNoPager: parseResult.GetValue(RootCommandBuilder.NoPagerOption),
                    ct: ct);

                var initial = parseResult.GetValue(inputArg) ?? string.Empty;
                var queue = parseResult.GetValue(queueOpt);
                var limit = parseResult.GetValue(limitOpt);
                if (limit < 1)
                {
                    limit = 1;
                }

                // Явно названная очередь вне allowed_queues — ошибка политики, а не пустая
                // выдача (HTTP-guard отклонил бы и сам запрос, но сообщение здесь точнее).
                QueueScopeFilter.EnsureQueueAllowed(queue, ctx.Profile);

                var loop = new SuggestLoop(ctx.Client, queue, limit, initial, ctx.Profile.AllowedQueues);
                var result = await loop.RunAsync(ct);
                if (result.PickedKey is null)
                {
                    return result.ExitCode;
                }
                Console.WriteLine(result.PickedKey);
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
    /// Строит relative path (без leading slash) для запроса <c>GET /v3/issues/_suggest</c>.
    /// Внешний клиент уже знает базовый URL и префикс <c>/v3/</c>.
    /// </summary>
    /// <param name="input">Текст поиска (может быть пустым).</param>
    /// <param name="queue">Опциональный фильтр по очереди.</param>
    /// <returns>Относительный путь с querystring.</returns>
    internal static string BuildSuggestPath(string input, string? queue)
    {
        var encodedInput = Uri.EscapeDataString(input);
        const string fields = "&full=true";
        if (string.IsNullOrEmpty(queue))
        {
            return $"issues/_suggest?input={encodedInput}{fields}";
        }
        return $"issues/_suggest?input={encodedInput}&queue={Uri.EscapeDataString(queue)}{fields}";
    }

    /// <summary>
    /// Отбирает элементы ответа <c>_suggest</c>, допустимые к показу, и обрезает их по лимиту.
    /// </summary>
    /// <remarks>
    /// Без <c>--queue</c> запрос уходит без указания очереди, поэтому HTTP-guard его
    /// пропускает (сегмент <c>_suggest</c> ключа не несёт), а сервер возвращает задачи любых
    /// очередей. Ограничение профиля должно действовать на результат — так же, как в
    /// <c>issue find</c>. Отсечение по <c>limit</c> происходит уже после фильтрации, иначе
    /// чужие задачи занимали бы места в списке.
    /// </remarks>
    /// <param name="payload">Ответ API (ожидается массив задач).</param>
    /// <param name="limit">Сколько результатов показывать.</param>
    /// <param name="allowedQueues">Разрешённые очереди профиля; пустой список = без ограничения.</param>
    /// <returns>Элементы для показа (клонированные, независимые от исходного документа).</returns>
    internal static JsonElement[] FilterSuggestions(
        JsonElement payload,
        int limit,
        IReadOnlyList<string>? allowedQueues)
    {
        if (payload.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<JsonElement>();
        }

        var list = new List<JsonElement>();
        foreach (var item in payload.EnumerateArray())
        {
            if (!QueueScopeFilter.AllowsIssue(item, allowedQueues))
            {
                continue;
            }

            list.Add(item.Clone());
            if (list.Count >= limit)
            {
                break;
            }
        }

        return list.ToArray();
    }

    /// <summary>
    /// Результат интерактивной TUI-сессии.
    /// </summary>
    private readonly record struct LoopResult(string? PickedKey, int ExitCode);

    /// <summary>
    /// Инкапсулирует state и render-цикл интерактивного suggest-TUI.
    /// </summary>
    private sealed class SuggestLoop
    {
        private readonly TrackerClient _client;
        private readonly string? _queue;
        private readonly int _limit;
        private readonly IReadOnlyList<string>? _allowedQueues;
        private string _buffer;
        private int _selectedIndex;
        private JsonElement[] _results = Array.Empty<JsonElement>();
        private string? _errorText;
        private bool _isSearching;
        private CancellationTokenSource? _debounceCts;
        private long _requestVersion;
        private readonly ConcurrentQueue<Action> _pendingUpdates = new();

        public SuggestLoop(
            TrackerClient client,
            string? queue,
            int limit,
            string initialBuffer,
            IReadOnlyList<string>? allowedQueues)
        {
            _client = client;
            _queue = queue;
            _limit = limit;
            _allowedQueues = allowedQueues;
            // L3: strip control chars from initial buffer.
            _buffer = new string((initialBuffer ?? string.Empty).Where(c => !char.IsControl(c)).ToArray());
            _selectedIndex = 0;
        }

        public async Task<LoopResult> RunAsync(CancellationToken ct)
        {
            string? pickedKey = null;
            var exitCode = 0;
            var done = false;

            try
            {
                await AnsiConsole.Live(BuildPanel())
                    .AutoClear(true)
                    .StartAsync(async live =>
                    {
                        // Если есть начальный текст — стартуем фетч сразу.
                        if (_buffer.Length > 0)
                        {
                            ScheduleFetch(ct);
                        }
                        DrainPendingUpdates();
                        // Initial render so the empty prompt + footer show immediately.
                        live.UpdateTarget(BuildPanel());

                        Task<ConsoleKeyInfo>? readKeyTask = null;
                        while (!done && !ct.IsCancellationRequested)
                        {
                            // Console.ReadKey блокирующий; читаем в отдельной таске,
                            // чтобы не мешать debounced-фетчам обновлять Live.
                            // ReadKey is not cancellable; relies on process exit on Ctrl+C.
                            readKeyTask ??= Task.Run(() => Console.ReadKey(intercept: true), ct);

                            // Drain any state mutations queued by background fetches before
                            // possibly waiting — keeps Live in sync with background fetches
                            // even when the user isn't pressing keys.
                            if (DrainPendingUpdates())
                            {
                                live.UpdateTarget(BuildPanel());
                            }

                            // Wait for either a key or a short tick (so background updates
                            // surface within ~80ms even without a keypress).
                            var winner = await Task.WhenAny(readKeyTask, Task.Delay(80, ct));
                            if (winner != readKeyTask)
                            {
                                // Timer tick — loop back, drain & render again.
                                continue;
                            }

                            var key = await readKeyTask;
                            readKeyTask = null;

                            switch (key.Key)
                            {
                                case ConsoleKey.Enter:
                                    if (_results.Length > 0)
                                    {
                                        if (_results[_selectedIndex].TryGetProperty("key", out var keyEl)
                                            && keyEl.ValueKind == JsonValueKind.String)
                                        {
                                            pickedKey = keyEl.GetString();
                                        }
                                    }
                                    done = true;
                                    break;
                                case ConsoleKey.Escape:
                                    exitCode = 130;
                                    done = true;
                                    break;
                                case ConsoleKey.UpArrow:
                                    if (_selectedIndex > 0)
                                    {
                                        _selectedIndex--;
                                    }
                                    break;
                                case ConsoleKey.DownArrow:
                                    if (_selectedIndex < _results.Length - 1)
                                    {
                                        _selectedIndex++;
                                    }
                                    break;
                                case ConsoleKey.Backspace:
                                    if (_buffer.Length > 0)
                                    {
                                        _buffer = _buffer[..^1];
                                        _errorText = null;
                                        ScheduleFetch(ct);
                                    }
                                    break;
                                default:
                                    if (!char.IsControl(key.KeyChar))
                                    {
                                        _buffer += key.KeyChar;
                                        _errorText = null;
                                        ScheduleFetch(ct);
                                    }
                                    break;
                            }

                            DrainPendingUpdates();
                            live.UpdateTarget(BuildPanel());
                        }
                    });
            }
            finally
            {
                // M1: dispose the last debounce CTS instance.
                try { _debounceCts?.Cancel(); } catch { /* already disposed */ }
                _debounceCts?.Dispose();
                _debounceCts = null;
            }

            return new LoopResult(pickedKey, exitCode);
        }

        /// <summary>
        /// Drains pending state mutations queued by background fetch tasks.
        /// Must only be called from the main key loop — this is what keeps
        /// <see cref="_results"/>/<see cref="_selectedIndex"/>/<see cref="_errorText"/>
        /// single-threaded.
        /// </summary>
        /// <returns><c>true</c> if any mutation was applied; otherwise <c>false</c>.
        /// Used by the main loop to skip unnecessary redraws on idle ticks.</returns>
        private bool DrainPendingUpdates()
        {
            var any = false;
            while (_pendingUpdates.TryDequeue(out var action))
            {
                action();
                any = true;
            }
            return any;
        }

        private void ScheduleFetch(CancellationToken outerCt)
        {
            // M2: cancel previous CTS but do NOT dispose it synchronously — the
            // background task may still observe its token. The task disposes it
            // in its own finally block.
            try { _debounceCts?.Cancel(); } catch (ObjectDisposedException) { /* already disposed */ }
            var cts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
            _debounceCts = cts;
            var version = Interlocked.Increment(ref _requestVersion);
            var bufferSnapshot = _buffer;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DebounceMilliseconds, cts.Token);
                    if (cts.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    if (string.IsNullOrEmpty(bufferSnapshot))
                    {
                        EnqueueIfCurrent(version, () =>
                        {
                            _results = Array.Empty<JsonElement>();
                            _selectedIndex = 0;
                            _errorText = null;
                            _isSearching = false;
                        });
                        return;
                    }

                    EnqueueIfCurrent(version, () => _isSearching = true);

                    var path = BuildSuggestPath(bufferSnapshot, _queue);
                    var payload = await _client.GetAsync(path, cts.Token);

                    // Игнорируем устаревшие ответы: если за время запроса успел уйти ещё один.
                    if (Volatile.Read(ref _requestVersion) != version)
                    {
                        return;
                    }

                    if (payload.ValueKind == JsonValueKind.Array)
                    {
                        var newResults = FilterSuggestions(payload, _limit, _allowedQueues);
                        EnqueueIfCurrent(version, () =>
                        {
                            _results = newResults;
                            _selectedIndex = _results.Length == 0
                                ? 0
                                : Math.Min(_selectedIndex, _results.Length - 1);
                            _errorText = null;
                            _isSearching = false;
                        });
                    }
                    else
                    {
                        EnqueueIfCurrent(version, () =>
                        {
                            _results = Array.Empty<JsonElement>();
                            _selectedIndex = 0;
                            _isSearching = false;
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    // Debounce reset — нормальное поведение. Новый запрос выставит свой флаг.
                }
                catch (TrackerException ex)
                {
                    EnqueueIfCurrent(version, () =>
                    {
                        _errorText = ex.Message;
                        _isSearching = false;
                    });
                }
                catch (Exception ex)
                {
                    // M3: surface unexpected errors instead of silently breaking suggestions.
                    EnqueueIfCurrent(version, () =>
                    {
                        _errorText = ex.Message;
                        _isSearching = false;
                    });
                }
                finally
                {
                    // M2: dispose this CTS only after the task is done observing its token.
                    cts.Dispose();
                }
            }, CancellationToken.None);
        }

        private void EnqueueIfCurrent(long version, Action mutation)
        {
            if (Volatile.Read(ref _requestVersion) != version)
            {
                return;
            }
            _pendingUpdates.Enqueue(() =>
            {
                if (Volatile.Read(ref _requestVersion) != version)
                {
                    return;
                }
                mutation();
            });
        }

        private IRenderable BuildPanel()
        {
            var grid = new Grid();
            grid.AddColumn();

            var prompt = $"[bold]🔎[/] [yellow]{Markup.Escape(_buffer)}[/][dim]_[/]";
            grid.AddRow(new Markup(prompt));

            if (string.IsNullOrEmpty(_buffer))
            {
                grid.AddRow(new Markup("[dim]Введите текст для поиска…[/]"));
            }
            else if (_results.Length == 0 && _errorText is null)
            {
                grid.AddRow(new Markup("[dim]Нет результатов.[/]"));
            }
            else
            {
                var table = new Table().Border(TableBorder.Minimal).Expand();
                table.AddColumn(new TableColumn("Key").NoWrap());
                table.AddColumn(new TableColumn("Summary"));
                table.AddColumn(new TableColumn("Status").NoWrap());
                table.AddColumn(new TableColumn("Assignee").NoWrap());
                for (var i = 0; i < _results.Length; i++)
                {
                    var item = _results[i];
                    var key = ReadString(item, "key") ?? "?";
                    var summary = FirstLine(ReadString(item, "summary") ?? string.Empty);
                    var status = ReadDisplayLike(item, "status");
                    var assignee = ReadDisplayLike(item, "assignee");

                    var selected = i == _selectedIndex;
                    table.AddRow(
                        FormatCell(key, selected, "cyan"),
                        FormatCell(summary, selected, "white"),
                        FormatCell(status, selected, "green"),
                        FormatCell(assignee, selected, "magenta"));
                }
                grid.AddRow(table);
            }

            if (_errorText is not null)
            {
                grid.AddRow(new Markup($"[red]{Markup.Escape(_errorText)}[/]"));
            }

            if (_isSearching)
            {
                grid.AddRow(new Markup("[yellow]⏳ Поиск…[/]"));
            }

            grid.AddRow(new Markup("[dim]↑↓ navigate · Enter pick · Esc cancel[/]"));

            return new Panel(grid)
                .Header("[bold]yt suggest[/]")
                .Border(BoxBorder.Rounded);
        }

        private static IRenderable FormatCell(string text, bool selected, string color)
        {
            var escaped = Markup.Escape(text);
            return selected
                ? new Markup($"[invert {color}]{escaped}[/]")
                : new Markup($"[{color}]{escaped}[/]");
        }

        private static string FirstLine(string text, int maxLen = 100)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var nl = text.IndexOfAny(new[] { '\r', '\n' });
            var line = nl >= 0 ? text.Substring(0, nl) : text;
            if (line.Length > maxLen) line = line.Substring(0, maxLen - 1) + "…";
            return line;
        }

        private static string? ReadString(JsonElement el, string prop)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            if (!el.TryGetProperty(prop, out var v)) return null;
            return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        private static string ReadDisplayLike(JsonElement el, string prop)
        {
            if (el.ValueKind != JsonValueKind.Object) return string.Empty;
            if (!el.TryGetProperty(prop, out var v)) return string.Empty;
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? string.Empty;
            if (v.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "display", "name", "key", "id" })
                {
                    if (v.TryGetProperty(name, out var inner) && inner.ValueKind == JsonValueKind.String)
                        return inner.GetString() ?? string.Empty;
                }
            }
            return string.Empty;
        }
    }
}
