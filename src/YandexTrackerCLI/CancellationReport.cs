namespace YandexTrackerCLI;

using Core.Api.Errors;

/// <summary>
/// Причина, по которой команда не доработала до конца.
/// </summary>
internal enum CancellationCause
{
    /// <summary>
    /// Отличить не удалось: сигнала не наблюдалось, вызывающий отмену не запрашивал,
    /// и в цепочке исключений нет <see cref="TimeoutException"/>. Сообщение остаётся общим.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Отмена пользователем или супервизором процесса: SIGINT (Ctrl-C) или SIGTERM.
    /// </summary>
    Interrupted,

    /// <summary>
    /// Сработал HTTP-таймаут (<c>--timeout</c> / <c>YT_TIMEOUT</c> / дефолт).
    /// </summary>
    Timeout,

    /// <summary>
    /// Отмену запросил вызывающий через переданный <see cref="CancellationToken"/>
    /// (in-process хост, тесты).
    /// </summary>
    HostRequested,

    /// <summary>
    /// Команда не свернулась за отведённое после сигнала время (или сигнал пришёл повторно),
    /// и вызов был прекращён принудительно.
    /// </summary>
    Unresponsive,
}

/// <summary>
/// Классифицирует <see cref="OperationCanceledException"/>, всплывшую из команды, и строит
/// по ней <see cref="TrackerException"/> с кодом <see cref="ErrorCode.Cancelled"/>.
/// </summary>
internal static class CancellationReport
{
    /// <summary>
    /// Предел обхода цепочки <c>InnerException</c> при поиске маркера таймаута.
    /// </summary>
    private const int MaxCauseDepth = 16;

    /// <summary>
    /// Определяет причину отмены.
    /// </summary>
    /// <param name="ex">Пойманное исключение отмены.</param>
    /// <param name="signalName">Имя пришедшего сигнала (<see cref="InterruptWatch.SignalName"/>) или <see langword="null"/>.</param>
    /// <param name="hostToken">Токен, переданный вызывающим в invoke.</param>
    /// <returns>Классифицированная причина.</returns>
    /// <remarks>
    /// Порядок проверок важен. Таймаут распознаётся первым и по самому надёжному признаку:
    /// <c>HttpClient</c> при истечении <c>Timeout</c> бросает <see cref="TaskCanceledException"/>
    /// с внутренним <see cref="TimeoutException"/> — этот маркер не подделывается отменой
    /// по токену. Только если маркера нет, отмена приписывается сигналу или вызывающему.
    /// </remarks>
    public static CancellationCause Classify(
        OperationCanceledException ex,
        string? signalName,
        CancellationToken hostToken)
    {
        if (HasTimeoutMarker(ex, depth: 0))
        {
            return CancellationCause.Timeout;
        }

        if (signalName is not null)
        {
            return CancellationCause.Interrupted;
        }

        if (hostToken.IsCancellationRequested)
        {
            return CancellationCause.HostRequested;
        }

        return CancellationCause.Unknown;
    }

    /// <summary>
    /// Строит ошибку отмены с сообщением, соответствующим установленной причине.
    /// </summary>
    /// <param name="cause">Причина отмены.</param>
    /// <param name="signalName">Имя сигнала для <see cref="CancellationCause.Interrupted"/>.</param>
    /// <param name="timeoutSeconds">Эффективный HTTP-таймаут в секундах — попадает в текст,
    /// чтобы «просто оборвалось» превращалось в «оборвалось через N секунд, поднимай так».</param>
    /// <param name="inner">Исходное исключение отмены.</param>
    /// <returns>Ошибка с кодом <see cref="ErrorCode.Cancelled"/> (exit 11).</returns>
    public static TrackerException ToException(
        CancellationCause cause,
        string? signalName,
        int timeoutSeconds,
        Exception? inner = null)
    {
        var raiseHint =
            $"raise it with --timeout <seconds> or the YT_TIMEOUT environment variable (currently {timeoutSeconds}s)";

        var message = cause switch
        {
            CancellationCause.Timeout =>
                $"timed out after {timeoutSeconds}s: the request did not complete within the HTTP timeout — {raiseHint}",
            CancellationCause.Interrupted =>
                $"cancelled by {signalName ?? "SIGINT"}: the command stopped before completing, its result is incomplete",
            CancellationCause.HostRequested =>
                "cancelled by the caller: the command stopped before completing, its result is incomplete",
            CancellationCause.Unresponsive =>
                $"cancelled by {signalName ?? "signal"}: the command did not stop in time and was terminated forcibly, "
                + "its result is incomplete and any operation already sent to the server may or may not have been applied",
            _ =>
                "cancelled before completing, its result is incomplete; the cause could not be determined "
                + $"(no termination signal was observed and no timeout was reported). If this was a timeout, {raiseHint}",
        };

        return new TrackerException(ErrorCode.Cancelled, message, inner: inner);
    }

    /// <summary>
    /// Ищет в цепочке исключений маркер таймаута <see cref="TimeoutException"/>.
    /// </summary>
    /// <param name="ex">Корневое исключение.</param>
    /// <param name="depth">Уже пройденная глубина цепочки.</param>
    /// <returns><see langword="true"/>, если таймаут найден.</returns>
    /// <remarks>
    /// Глубина ограничена <see cref="MaxCauseDepth"/> и передаётся в рекурсивный вызов:
    /// иначе цепочка вида <c>Aggregate → Aggregate → …</c> обнуляла бы счётчик на каждом
    /// уровне и цикл inner-ссылок сносил бы процесс <c>StackOverflowException</c>,
    /// который в .NET не перехватывается.
    /// </remarks>
    private static bool HasTimeoutMarker(Exception? ex, int depth)
    {
        while (ex is not null && depth < MaxCauseDepth)
        {
            if (ex is TimeoutException)
            {
                return true;
            }

            if (ex is AggregateException agg)
            {
                foreach (var candidate in agg.InnerExceptions)
                {
                    if (HasTimeoutMarker(candidate, depth + 1))
                    {
                        return true;
                    }
                }

                return false;
            }

            ex = ex.InnerException;
            depth++;
        }

        return false;
    }
}
