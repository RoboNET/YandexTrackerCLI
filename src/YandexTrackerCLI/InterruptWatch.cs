namespace YandexTrackerCLI;

using System.Runtime.InteropServices;

/// <summary>
/// Обработчик сигналов завершения процесса (SIGINT/Ctrl-C, SIGTERM) для <see cref="CliRunner"/>:
/// запоминает пришедший сигнал, отменяет выполнение команды и, если та не свернулась
/// за отведённое время, сообщает об этом наверх.
/// </summary>
/// <remarks>
/// <para>
/// Обработку сигналов приходится держать здесь, а не полагаться на встроенную в
/// <c>System.CommandLine</c> (<c>InvocationConfiguration.ProcessTerminationTimeout</c>),
/// именно ради различения причин отмены. .NET вызывает обработчики сигналов в порядке,
/// обратном регистрации, а обработчик <c>System.CommandLine</c> регистрируется позже
/// (уже внутри invoke) и внутри себя блокируется в ожидании завершения команды. Поэтому
/// сторонний «пассивный наблюдатель» получает управление только после того, как команда
/// уже упала с <see cref="OperationCanceledException"/> — то есть всегда слишком поздно,
/// чтобы сказать, был ли сигнал. Проверено на собранном бинаре: факт SIGTERM не успевал
/// записаться, и отмена классифицировалась как «причина неизвестна».
/// </para>
/// <para>
/// Логика повторяет встроенную: сигнал подавляется (<see cref="PosixSignalContext.Cancel"/>),
/// отменяется токен вызова, и команде даётся grace-период на то, чтобы свернуться самой.
/// Сигнал подавляется только когда есть что сворачивать — см. <see cref="Handle"/>.
/// </para>
/// <para>
/// Обработчик не блокирует поток доставки сигналов. .NET диспатчит posix-сигналы одним
/// выделенным потоком последовательно, поэтому ожидание команды прямо в обработчике
/// сделало бы второй Ctrl-C неработающим ровно на те две секунды, ради которых его жмут:
/// сигнал не был бы доставлен, пока первый обработчик спит. Ожидание grace-периода вынесено
/// в отдельную задачу, а обработчик возвращается сразу.
/// </para>
/// </remarks>
internal sealed class InterruptWatch : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly TimeSpan _gracePeriod;
    private readonly TaskCompletionSource _unresponsive = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _invocation;
    private PosixSignalRegistration? _sigInt;
    private PosixSignalRegistration? _sigTerm;
    private ConsoleCancelEventHandler? _cancelKeyPress;
    private string? _signalName;
    private int _disposed;

    private InterruptWatch(CancellationTokenSource cts, TimeSpan gracePeriod)
    {
        _cts = cts;
        _gracePeriod = gracePeriod;
    }

    /// <summary>
    /// Имя пришедшего сигнала (<c>SIGINT</c> / <c>SIGTERM</c>) либо <see langword="null"/>,
    /// если сигнала не было. Первый пришедший сигнал побеждает.
    /// </summary>
    public string? SignalName => Volatile.Read(ref _signalName);

    /// <summary>
    /// Завершается, когда команда не свернулась за grace-период после сигнала.
    /// До этого момента задача не завершена. Повторный сигнал сюда не приходит:
    /// его не подавляют, и процесс завершает ОС.
    /// </summary>
    public Task Unresponsive => _unresponsive.Task;

    /// <summary>
    /// Начинает обработку сигналов завершения.
    /// </summary>
    /// <param name="cts">Источник отмены вызова, который следует отменить по сигналу.</param>
    /// <param name="gracePeriod">Сколько ждать самостоятельного завершения команды после сигнала.</param>
    /// <returns>Обработчик, который следует освободить по завершении вызова.</returns>
    /// <remarks>
    /// Если платформа не поддерживает ни <see cref="PosixSignalRegistration"/>, ни
    /// <see cref="Console.CancelKeyPress"/>, обработчик остаётся пустышкой: отмена по сигналу
    /// не перехватывается, процесс завершается штатной реакцией ОС — как и до появления этого кода.
    /// </remarks>
    public static InterruptWatch Start(CancellationTokenSource cts, TimeSpan gracePeriod)
    {
        var watch = new InterruptWatch(cts, gracePeriod);
        try
        {
            watch._sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, watch.OnPosixSignal);
            watch._sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, watch.OnPosixSignal);
            return watch;
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or NotSupportedException or IOException)
        {
            watch._sigInt?.Dispose();
            watch._sigInt = null;
            watch._sigTerm = null;
        }

        try
        {
            void Handler(object? sender, ConsoleCancelEventArgs e) =>
                e.Cancel = watch.Handle(PosixSignal.SIGINT);

            Console.CancelKeyPress += Handler;
            watch._cancelKeyPress = Handler;
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or NotSupportedException or IOException)
        {
            // Наблюдать нечем — остаёмся с «сигнала не было».
        }

        return watch;
    }

    /// <summary>
    /// Сообщает обработчику задачу выполнения команды: именно её он ждёт после сигнала,
    /// прежде чем признать вызов не отвечающим.
    /// </summary>
    /// <param name="invocation">Задача выполнения команды.</param>
    /// <remarks>
    /// Сигнал, пришедший до этого вызова, отменит токен, но ждать ему будет нечего —
    /// команда завершится сама по отменённому токену.
    /// </remarks>
    public void Track(Task invocation) => Volatile.Write(ref _invocation, invocation);

    private void OnPosixSignal(PosixSignalContext context) => context.Cancel = Handle(context.Signal);

    /// <summary>
    /// Обрабатывает сигнал завершения.
    /// </summary>
    /// <param name="signal">Пришедший сигнал.</param>
    /// <returns>
    /// <see langword="true"/>, если штатную реакцию ОС следует подавить (мы берём завершение
    /// на себя), и <see langword="false"/>, если сигнал должен убить процесс как обычно.
    /// </returns>
    /// <remarks>
    /// Подавлять сигнал можно, только когда есть что сворачивать. Два случая, когда нельзя:
    /// <list type="bullet">
    ///   <item><description>
    ///     команда ещё не дошла до первого await и выполняется синхронно прямо в вызывающем
    ///     потоке (например, читает stdin блокирующим <c>ReadToEnd</c>). Отменять нечего,
    ///     а основной поток занят и всё равно не напечатает ошибку — подавив сигнал, мы бы
    ///     сделали CLI неубиваемым по Ctrl-C;
    ///   </description></item>
    ///   <item><description>
    ///     сигнал повторный: пользователь уже просил остановиться и ждать не готов.
    ///   </description></item>
    /// </list>
    /// В обоих случаях процесс завершает ОС — ровно как до появления этого кода.
    /// Метод обязан возвращаться немедленно: он выполняется на потоке доставки сигналов,
    /// и любая задержка в нём откладывает доставку следующего сигнала.
    /// </remarks>
    internal bool Handle(PosixSignal signal)
    {
        var invocation = Volatile.Read(ref _invocation);
        if (invocation is null)
        {
            return false;
        }

        var name = signal == PosixSignal.SIGTERM ? "SIGTERM" : "SIGINT";
        if (Interlocked.CompareExchange(ref _signalName, name, null) is not null)
        {
            return false;
        }

        try
        {
            _cts.Cancel();
        }
        catch (Exception)
        {
            // CancellationTokenSource.Cancel() исполняет зарегистрированные колбэки синхронно
            // на вызывающем потоке — то есть здесь, на потоке доставки сигналов. Чужой колбэк,
            // бросивший исключение, прилетит сюда AggregateException'ом, а уже завершившийся
            // вызов — ObjectDisposedException. Необработанное исключение на этом потоке валит
            // процесс, поэтому ловим широко: отмена всё равно уже запрошена или не нужна.
        }

        WatchGracePeriod(invocation);
        return true;
    }

    /// <summary>
    /// Асинхронно ждёт завершения команды в течение grace-периода и, если не дождался,
    /// объявляет вызов не отвечающим.
    /// </summary>
    /// <param name="invocation">Задача выполнения команды.</param>
    private void WatchGracePeriod(Task invocation)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await invocation.WaitAsync(_gracePeriod).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _unresponsive.TrySetResult();
            }
            catch (Exception)
            {
                // Команда завершилась исключением — его разберёт основной поток.
            }
        });
    }

    /// <summary>
    /// Снимает регистрации обработчиков сигналов. Идемпотентен.
    /// </summary>
    /// <remarks>
    /// После освобождения регистрации уже начавшийся обработчик может ещё доработать —
    /// это безопасно: он лишь отменяет токен (ошибка отмены подавляется) и ставит
    /// признак не отвечающего вызова, который к этому моменту никто не читает.
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _sigInt?.Dispose();
        _sigInt = null;
        _sigTerm?.Dispose();
        _sigTerm = null;
        if (_cancelKeyPress is not null)
        {
            Console.CancelKeyPress -= _cancelKeyPress;
            _cancelKeyPress = null;
        }
    }
}
