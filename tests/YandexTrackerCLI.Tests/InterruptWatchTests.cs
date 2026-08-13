namespace YandexTrackerCLI.Tests;

using System.Diagnostics;
using System.Runtime.InteropServices;
using TUnit.Core;

/// <summary>
/// Тесты обработчика сигналов завершения. Сигнал подаётся вызовом
/// <see cref="InterruptWatch.Handle"/> напрямую, а не настоящим <c>kill</c>: настоящий SIGINT
/// в процессе тестового хоста, не подавленный обработчиком, убил бы весь прогон.
/// </summary>
public sealed class InterruptWatchTests
{
    /// <summary>
    /// Заведомо больший, чем любой разумный тест, grace-период: используется там, где
    /// проверяется, что обработчик в него НЕ упирается.
    /// </summary>
    private static readonly TimeSpan LongGrace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Первый сигнал: подавляется (завершение берём на себя), отменяет токен вызова
    /// и запоминается по имени.
    /// </summary>
    [Test]
    public async Task FirstSignal_IsSuppressed_CancelsToken_AndIsRemembered()
    {
        using var cts = new CancellationTokenSource();
        using var watch = InterruptWatch.Start(cts, LongGrace);
        var invocation = new TaskCompletionSource();
        watch.Track(invocation.Task);

        var suppressed = watch.Handle(PosixSignal.SIGINT);

        await Assert.That(suppressed).IsTrue();
        await Assert.That(cts.IsCancellationRequested).IsTrue();
        await Assert.That(watch.SignalName).IsEqualTo("SIGINT");
        invocation.SetResult();
    }

    /// <summary>
    /// SIGTERM от супервизора обрабатывается так же и называется своим именем.
    /// </summary>
    [Test]
    public async Task SigTerm_IsRememberedByItsOwnName()
    {
        using var cts = new CancellationTokenSource();
        using var watch = InterruptWatch.Start(cts, LongGrace);
        var invocation = new TaskCompletionSource();
        watch.Track(invocation.Task);

        watch.Handle(PosixSignal.SIGTERM);

        await Assert.That(watch.SignalName).IsEqualTo("SIGTERM");
        invocation.SetResult();
    }

    /// <summary>
    /// Регрессия на главное обещание Ctrl-C: обработчик не имеет права блокировать поток
    /// доставки сигналов на grace-период. .NET диспатчит posix-сигналы одним потоком
    /// последовательно, поэтому блокирующий обработчик означал бы, что второй Ctrl-C
    /// не доходит до процесса ровно те две секунды, ради которых его жмут.
    /// </summary>
    [Test]
    public async Task SecondSignal_ArrivesImmediately_AndIsNotSuppressed()
    {
        using var cts = new CancellationTokenSource();
        using var watch = InterruptWatch.Start(cts, LongGrace);

        // Команда, которая на отмену не реагирует: обработчику нечего дождаться.
        var invocation = new TaskCompletionSource();
        watch.Track(invocation.Task);

        var sw = Stopwatch.StartNew();
        var first = watch.Handle(PosixSignal.SIGINT);
        var second = watch.Handle(PosixSignal.SIGINT);
        sw.Stop();

        await Assert.That(first).IsTrue();

        // false = сигнал не подавляем, процесс убивает ОС — это и есть «второй Ctrl-C убивает».
        await Assert.That(second).IsFalse();

        // На блокирующей реализации оба вызова заняли бы LongGrace.
        await Assert.That(sw.Elapsed).IsLessThan(TimeSpan.FromSeconds(1));
        invocation.SetResult();
    }

    /// <summary>
    /// Сигнал до старта команды не подавляется: отменять нечего, а подавив его, CLI стал бы
    /// неубиваемым по Ctrl-C в момент синхронной работы (например, блокирующего чтения stdin).
    /// </summary>
    [Test]
    public async Task SignalBeforeInvocation_IsNotSuppressed_AndChangesNothing()
    {
        using var cts = new CancellationTokenSource();
        using var watch = InterruptWatch.Start(cts, LongGrace);

        var suppressed = watch.Handle(PosixSignal.SIGINT);

        await Assert.That(suppressed).IsFalse();
        await Assert.That(watch.SignalName).IsNull();
        await Assert.That(cts.IsCancellationRequested).IsFalse();
        await Assert.That(watch.Unresponsive.IsCompleted).IsFalse();
    }

    /// <summary>
    /// Команда, не свернувшаяся за grace-период, объявляется не отвечающей.
    /// </summary>
    [Test]
    public async Task GracePeriodElapsed_MarksInvocationUnresponsive()
    {
        using var cts = new CancellationTokenSource();
        using var watch = InterruptWatch.Start(cts, TimeSpan.FromMilliseconds(50));
        var invocation = new TaskCompletionSource();
        watch.Track(invocation.Task);

        watch.Handle(PosixSignal.SIGINT);

        await watch.Unresponsive.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(watch.Unresponsive.IsCompletedSuccessfully).IsTrue();
        invocation.SetResult();
    }

    /// <summary>
    /// Команда, успевшая свернуться в отведённое время, не отвечающей не считается —
    /// иначе штатная отмена печаталась бы как принудительное завершение.
    /// </summary>
    [Test]
    public async Task InvocationThatStopsInTime_IsNotMarkedUnresponsive()
    {
        using var cts = new CancellationTokenSource();
        using var watch = InterruptWatch.Start(cts, TimeSpan.FromMilliseconds(500));
        var invocation = new TaskCompletionSource();
        watch.Track(invocation.Task);

        watch.Handle(PosixSignal.SIGINT);
        invocation.SetResult();

        await Task.Delay(TimeSpan.FromSeconds(1));
        await Assert.That(watch.Unresponsive.IsCompleted).IsFalse();
    }

    /// <summary>
    /// Упавшая команда не считается не отвечающей: её исключение разбирает основной поток.
    /// </summary>
    [Test]
    public async Task InvocationThatFails_IsNotMarkedUnresponsive()
    {
        using var cts = new CancellationTokenSource();
        using var watch = InterruptWatch.Start(cts, TimeSpan.FromMilliseconds(500));
        var invocation = new TaskCompletionSource();
        watch.Track(invocation.Task);

        watch.Handle(PosixSignal.SIGINT);
        invocation.SetException(new InvalidOperationException("boom"));

        await Task.Delay(TimeSpan.FromSeconds(1));
        await Assert.That(watch.Unresponsive.IsCompleted).IsFalse();
    }

    /// <summary>
    /// Колбэк, зарегистрированный на токене отмены, исполняется синхронно внутри
    /// <see cref="CancellationTokenSource.Cancel"/> — то есть на потоке доставки сигналов.
    /// Его исключение не должно вылетать из обработчика: там оно валит процесс.
    /// </summary>
    [Test]
    public async Task ThrowingCancellationCallback_DoesNotEscapeTheHandler()
    {
        using var cts = new CancellationTokenSource();
        using var registration = cts.Token.Register(() => throw new InvalidOperationException("callback"));
        using var watch = InterruptWatch.Start(cts, TimeSpan.FromMilliseconds(50));
        var invocation = new TaskCompletionSource();
        watch.Track(invocation.Task);

        var suppressed = watch.Handle(PosixSignal.SIGINT);

        await Assert.That(suppressed).IsTrue();
        await Assert.That(watch.SignalName).IsEqualTo("SIGINT");
        invocation.SetResult();
    }

    /// <summary>
    /// Отмена уже освобождённого источника не считается ошибкой: вызов к этому моменту завершён.
    /// </summary>
    [Test]
    public async Task DisposedTokenSource_DoesNotEscapeTheHandler()
    {
        var cts = new CancellationTokenSource();
        using var watch = InterruptWatch.Start(cts, TimeSpan.FromMilliseconds(50));
        var invocation = new TaskCompletionSource();
        watch.Track(invocation.Task);
        cts.Dispose();

        var suppressed = watch.Handle(PosixSignal.SIGINT);

        await Assert.That(suppressed).IsTrue();
        invocation.SetResult();
    }

    /// <summary>
    /// <see cref="InterruptWatch.Dispose"/> идемпотентен: повторный вызов ничего не ломает.
    /// </summary>
    [Test]
    public async Task Dispose_IsIdempotent()
    {
        using var cts = new CancellationTokenSource();
        var watch = InterruptWatch.Start(cts, LongGrace);

        watch.Dispose();
        watch.Dispose();

        await Assert.That(watch.SignalName).IsNull();
    }
}
