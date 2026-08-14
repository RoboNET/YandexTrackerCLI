namespace YandexTrackerCLI;

using System.CommandLine;
using Microsoft.Win32.SafeHandles;
using Commands;
using Core.Api.Errors;
using Output;

/// <summary>
/// Единственная точка входа CLI: разбирает аргументы, выполняет команду и переводит
/// всё, что не поймали сами команды, в стабильный контракт «JSON-ошибка на stderr + exit-код».
/// </summary>
/// <remarks>
/// <para>
/// Обработка отмены живёт здесь, а не в командах, сознательно. Отмена приходит как
/// <see cref="OperationCanceledException"/> из любого <c>await</c> внутри любой команды —
/// в том числе из кода, который команда не писала (HTTP-конвейер, чтение конфига, стриминг
/// тела ответа). Разложить <c>catch</c> по всем обработчикам значило бы продублировать его
/// десятки раз и получить новую команду без него при первом же добавлении. Верхний уровень
/// покрывает и уже написанные команды, и будущие.
/// </para>
/// <para>
/// Собственный обработчик исключений <c>System.CommandLine</c> здесь выключен
/// (<see cref="InvocationConfiguration.EnableDefaultExceptionHandler"/>): он молча возвращает 1
/// на любую отмену и печатает <c>Exception.ToString()</c> на всё остальное — ни то, ни другое
/// не соответствует таблице exit-кодов.
/// </para>
/// <para>
/// Обработка сигналов завершения тоже своя (<see cref="InterruptWatch"/>), а встроенная
/// выключена (<see cref="InvocationConfiguration.ProcessTerminationTimeout"/> = <c>null</c>).
/// Иначе отмену по сигналу нельзя отличить от таймаута: встроенный обработчик регистрируется
/// позже нашего и вызывается раньше (обработчики сигналов в .NET идут в обратном порядке
/// регистрации), внутри себя дожидается завершения команды — и к моменту, когда мы могли бы
/// узнать о сигнале, ошибка уже классифицирована. Подробнее — в <see cref="InterruptWatch"/>.
/// </para>
/// </remarks>
public static class CliRunner
{
    /// <summary>
    /// Сколько ждать самостоятельного завершения команды после сигнала, прежде чем
    /// признать её не отвечающей. Совпадает с дефолтом <c>System.CommandLine</c>,
    /// который эта обработка заменяет.
    /// </summary>
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Разбирает аргументы и выполняет команду, приводя необработанные исключения
    /// к контракту ошибок CLI.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    /// <param name="configuration">Конфигурация вызова; <see langword="null"/> — конфигурация
    /// по умолчанию. Поле <see cref="InvocationConfiguration.EnableDefaultExceptionHandler"/>
    /// принудительно выставляется в <see langword="false"/>.</param>
    /// <param name="ct">Токен отмены вызывающего (в проде — <c>default</c>; сигналы
    /// обрабатывает <see cref="InterruptWatch"/>).</param>
    /// <param name="gracePeriod">Сколько ждать самостоятельного завершения команды после
    /// сигнала; <see langword="null"/> — <see cref="DefaultGracePeriod"/>.</param>
    /// <returns>Exit-код процесса.</returns>
    public static Task<int> Run(
        string[] args,
        InvocationConfiguration? configuration = null,
        CancellationToken ct = default,
        TimeSpan? gracePeriod = null) =>
        Run(args, configuration, ct, gracePeriod, onWatchReady: null);

    /// <summary>
    /// Вариант <see cref="Run(string[], InvocationConfiguration?, CancellationToken, TimeSpan?)"/>
    /// с хуком на созданный <see cref="InterruptWatch"/> — чтобы тест мог подать сигнал,
    /// не посылая процессу настоящий SIGINT.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    /// <param name="configuration">Конфигурация вызова.</param>
    /// <param name="ct">Токен отмены вызывающего.</param>
    /// <param name="gracePeriod">Grace-период; <see langword="null"/> — <see cref="DefaultGracePeriod"/>.</param>
    /// <param name="onWatchReady">Вызывается сразу после запуска обработчика сигналов.</param>
    /// <returns>Exit-код процесса.</returns>
    internal static async Task<int> Run(
        string[] args,
        InvocationConfiguration? configuration,
        CancellationToken ct,
        TimeSpan? gracePeriod,
        Action<InterruptWatch>? onWatchReady)
    {
        var config = configuration ?? new InvocationConfiguration();
        config.EnableDefaultExceptionHandler = false;
        config.ProcessTerminationTimeout = null;

        ParseResult? parseResult = null;
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Команда, брошенная на grace-периоде, продолжает пользоваться linked-токеном уже
        // после выхода отсюда, поэтому в этом (и только в этом) случае CTS не освобождается:
        // процесс всё равно завершается следующим оператором.
        var abandoned = false;
        using var interrupts = InterruptWatch.Start(linked, gracePeriod ?? DefaultGracePeriod);
        onWatchReady?.Invoke(interrupts);
        try
        {
            // Разбор аргументов тоже внутри try: CliRunner — единственная точка, приводящая
            // всё к контракту ошибок, и сбой разбора не должен из неё вываливаться сырым.
            parseResult = RootCommandBuilder.Build().Parse(args);

            var invocation = parseResult.InvokeAsync(config, linked.Token);
            interrupts.Track(invocation);

            if (await Task.WhenAny(invocation, interrupts.Unresponsive) != invocation
                && !invocation.IsCompleted)
            {
                // Команда проигнорировала отмену. Дожидаться её дальше нельзя — иначе CLI
                // выглядит как зависший на Ctrl-C. Печатает основной поток, а не обработчик
                // сигнала, чтобы вывод не переплетался с выводом самой команды.
                // Повторная проверка IsCompleted сужает окно, в котором команда успевает
                // напечатать свою ошибку уже после того, как мы напечатали свою.
                abandoned = true;
                var stuck = CancellationReport.ToException(
                    CancellationCause.Unresponsive,
                    interrupts.SignalName,
                    ResolveTimeoutSeconds(parseResult));
                await ReportAbandoned(config, stuck);
                return stuck.Code.ToExitCode();
            }

            return await invocation;
        }
        catch (OperationCanceledException ex)
        {
            var cause = CancellationReport.Classify(ex, interrupts.SignalName, ct);
            var error = CancellationReport.ToException(
                cause,
                interrupts.SignalName,
                ResolveTimeoutSeconds(parseResult),
                ex);
            ErrorWriter.Write(config.Error, error);
            Finish(config);
            return error.Code.ToExitCode();
        }
        catch (TrackerException ex)
        {
            // Страховка: команда бросила доменную ошибку мимо собственного catch
            // (например, из блока освобождения ресурсов). Контракт вывода тот же.
            ErrorWriter.Write(config.Error, ex);
            Finish(config);
            return ex.Code.ToExitCode();
        }
        catch (Exception ex)
        {
            // Непредвиденный сбой: печатаем исключение целиком — это дефект CLI,
            // и стектрейс здесь единственная полезная информация. Exit 1 как и раньше.
            config.Error.WriteLine("Unhandled exception: " + ex);
            Finish(config);
            return 1;
        }
        finally
        {
            if (!abandoned)
            {
                linked.Dispose();
            }
        }
    }

    /// <summary>
    /// Сколько ждать, пока напечатается сообщение о брошенной команде, прежде чем выйти без него.
    /// </summary>
    private static readonly TimeSpan ReportDeadline = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Печатает ошибку о брошенной команде, не позволяя самой печати подвесить процесс.
    /// </summary>
    /// <param name="config">Конфигурация вызова.</param>
    /// <param name="error">Ошибка, которую следует напечатать.</param>
    /// <returns>Задача печати, завершающаяся не позже <see cref="ReportDeadline"/>.</returns>
    /// <remarks>
    /// <para>
    /// Брошенная команда может держать блокировку вывода: на Unix <c>ConsolePal</c>
    /// сериализует записи и в stdout, и в stderr через один Monitor, и команда, залипшая
    /// в <c>write(2)</c> на переполненном пайпе (типичный <c>yt … | head</c> с неспешным
    /// потребителем), не отпустит его никогда. Печать «команда не отвечает» через
    /// <see cref="Console.Error"/> встала бы на том же локе — и CLI завис бы ровно там, где
    /// обязан был перестать ждать. Проверено на собранном бинаре: без обхода процесс не
    /// завершался вовсе, стек показывал <c>Monitor.Enter</c> внутри
    /// <c>ConsolePal.WriteFromConsoleStream</c>.
    /// </para>
    /// <para>
    /// Поэтому в проде сообщение пишется прямо в дескриптор stderr мимо <c>ConsolePal</c>,
    /// а сама печать всё равно ограничена дедлайном: если и это не проходит (stderr тоже
    /// заткнут), сообщение теряется, но exit-код доезжает — молчаливый выход с кодом 11
    /// лучше вечного ожидания.
    /// </para>
    /// </remarks>
    private static Task ReportAbandoned(InvocationConfiguration config, TrackerException error)
    {
        var payload = ErrorWriter.Render(error);
        var write = Task.Run(() =>
        {
            if (ReferenceEquals(config.Error, Console.Error) && TryWriteRawStderr(payload))
            {
                return;
            }

            config.Error.WriteLine(payload);
            Finish(config);
        });

        return Task.WhenAny(write, Task.Delay(ReportDeadline));
    }

    /// <summary>
    /// Пишет строку прямо в дескриптор stderr, минуя <see cref="Console"/> и его блокировку.
    /// </summary>
    /// <param name="payload">Сообщение без перевода строки.</param>
    /// <returns><see langword="true"/>, если запись удалась.</returns>
    private static bool TryWriteRawStderr(string payload)
    {
        if (OperatingSystem.IsWindows())
        {
            // Дескриптор 2 — понятие POSIX; на Windows обходить нечего: там записи
            // в консоль не сериализуются одним общим Monitor'ом.
            return false;
        }

        try
        {
            var prefix = !Console.IsErrorRedirected
                         && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))
                ? AnsiStyle.Reset
                : string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(prefix + payload + Environment.NewLine);

            using var handle = new SafeFileHandle((IntPtr)2, ownsHandle: false);
            using var stderr = new FileStream(handle, FileAccess.Write);
            stderr.Write(bytes);
            stderr.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Завершает аварийный выход: возвращает терминалу нормальные атрибуты и сбрасывает буферы.
    /// </summary>
    /// <param name="config">Конфигурация вызова, чьи writer'ы нужно сбросить.</param>
    /// <remarks>
    /// <para>
    /// Команда, прерванная посреди раскрашенного вывода, могла не успеть закрыть SGR-атрибут,
    /// и терминал остался бы, например, красным до следующего <c>reset</c>. Печатаем
    /// <c>ESC[0m</c>, но только когда stderr действительно терминал и цвет не запрещён
    /// (<c>NO_COLOR</c>): в пайп или в файл escape-последовательности не место.
    /// </para>
    /// <para>
    /// Сброс буферов обязателен на пути с брошенной командой: возврат из <c>Main</c> может
    /// не дождаться флаша чужих writer'ов, и сообщение об ошибке рискует не доехать.
    /// </para>
    /// </remarks>
    private static void Finish(InvocationConfiguration config)
    {
        try
        {
            if (!Console.IsErrorRedirected
                && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
            {
                config.Error.Write(AnsiStyle.Reset);
            }

            config.Error.Flush();
            config.Output.Flush();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Печатать больше некуда — молчим, exit-код всё равно донесёт факт ошибки.
        }
    }

    /// <summary>
    /// Восстанавливает эффективный HTTP-таймаут для текста сообщения об отмене:
    /// <c>--timeout</c> → env <c>YT_TIMEOUT</c> → дефолт.
    /// </summary>
    /// <param name="parseResult">Результат разбора аргументов; <see langword="null"/>,
    /// если разбор не дошёл до конца.</param>
    /// <returns>Таймаут в секундах.</returns>
    /// <remarks>
    /// Каскад берётся из <see cref="TimeoutResolver"/> — из того же места, откуда его берёт
    /// <see cref="TrackerContextFactory"/>, потому что контекст к моменту отмены уже разрушен,
    /// а называть в сообщении чужое число хуже, чем не называть вовсе. Недопустимое значение
    /// (<c>--timeout 0</c>, <c>YT_TIMEOUT=-5</c>) в текст не попадает: фабрика его отвергла бы,
    /// работал бы дефолт — его и называем.
    /// </remarks>
    private static int ResolveTimeoutSeconds(ParseResult? parseResult)
    {
        int? cli = null;
        try
        {
            cli = parseResult?.GetValue(RootCommandBuilder.TimeoutOption);
        }
        catch (InvalidOperationException)
        {
            // Опция не разобралась (разбор упал раньше) — падать обратно на env/дефолт.
        }

        return TimeoutResolver.ResolveForMessage(cli, EnvReader.Snapshot());
    }
}
