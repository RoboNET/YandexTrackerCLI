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

                var loop = new SuggestLoop(ctx.Client, queue, limit, initial);
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
        if (string.IsNullOrEmpty(queue))
        {
            return $"issues/_suggest?input={encodedInput}";
        }
        return $"issues/_suggest?input={encodedInput}&queue={Uri.EscapeDataString(queue)}";
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
        private string _buffer;
        private int _selectedIndex;
        private JsonElement[] _results = Array.Empty<JsonElement>();
        private string? _errorText;
        private CancellationTokenSource? _debounceCts;
        private long _requestVersion;
        private readonly ConcurrentQueue<Action> _pendingUpdates = new();

        public SuggestLoop(TrackerClient client, string? queue, int limit, string initialBuffer)
        {
            _client = client;
            _queue = queue;
            _limit = limit;
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
                        live.UpdateTarget(BuildPanel());

                        while (!done && !ct.IsCancellationRequested)
                        {
                            // Console.ReadKey блокирующий; читаем в отдельной таске,
                            // чтобы не мешать debounced-фетчам обновлять Live.
                            // ReadKey is not cancellable; relies on process exit on Ctrl+C.
                            var keyTask = Task.Run(() => Console.ReadKey(intercept: true), ct);
                            var key = await keyTask;

                            // Drain any state mutations queued by background fetches before
                            // reading shared state — keeps mutation single-threaded on this loop.
                            DrainPendingUpdates();

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
        private void DrainPendingUpdates()
        {
            while (_pendingUpdates.TryDequeue(out var action))
            {
                action();
            }
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
                        });
                        return;
                    }

                    var path = BuildSuggestPath(bufferSnapshot, _queue);
                    var payload = await _client.GetAsync(path, cts.Token);

                    // Игнорируем устаревшие ответы: если за время запроса успел уйти ещё один.
                    if (Volatile.Read(ref _requestVersion) != version)
                    {
                        return;
                    }

                    if (payload.ValueKind == JsonValueKind.Array)
                    {
                        var list = new List<JsonElement>();
                        foreach (var item in payload.EnumerateArray())
                        {
                            list.Add(item.Clone());
                            if (list.Count >= _limit)
                            {
                                break;
                            }
                        }
                        var newResults = list.ToArray();
                        EnqueueIfCurrent(version, () =>
                        {
                            _results = newResults;
                            _selectedIndex = _results.Length == 0
                                ? 0
                                : Math.Min(_selectedIndex, _results.Length - 1);
                            _errorText = null;
                        });
                    }
                    else
                    {
                        EnqueueIfCurrent(version, () =>
                        {
                            _results = Array.Empty<JsonElement>();
                            _selectedIndex = 0;
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    // Debounce reset — нормальное поведение.
                }
                catch (TrackerException ex)
                {
                    EnqueueIfCurrent(version, () => _errorText = ex.Message);
                }
                catch (Exception ex)
                {
                    // M3: surface unexpected errors instead of silently breaking suggestions.
                    EnqueueIfCurrent(version, () => _errorText = ex.Message);
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
                table.AddColumn("Key");
                table.AddColumn("Summary");
                table.AddColumn("Status");
                table.AddColumn("Assignee");
                for (var i = 0; i < _results.Length; i++)
                {
                    var item = _results[i];
                    var key = ReadString(item, "key") ?? "?";
                    var summary = ReadString(item, "summary") ?? string.Empty;
                    var status = ReadNested(item, "status", "display")
                                 ?? ReadNested(item, "status", "key")
                                 ?? string.Empty;
                    var assignee = ReadNested(item, "assignee", "display") ?? string.Empty;

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

        private static string? ReadString(JsonElement el, string prop)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            if (!el.TryGetProperty(prop, out var v)) return null;
            return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        private static string? ReadNested(JsonElement el, string parent, string child)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            if (!el.TryGetProperty(parent, out var p)) return null;
            return ReadString(p, child);
        }
    }
}
