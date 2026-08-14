namespace YandexTrackerCLI.Tests;

using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using TUnit.Core;
using Http;
using YandexTrackerCLI.Core.Api.Errors;

/// <summary>
/// End-to-end тесты верхнеуровневой обработки отмены в <see cref="CliRunner"/>:
/// прерванная команда обязана отдать JSON-ошибку <c>cancelled</c> и exit 11,
/// а не стектрейс .NET и не «общий» exit 1. Мутируют глобальное state
/// (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class CliRunnerCancellationTests
{
    /// <summary>
    /// Достаёт из stderr последнюю строку-JSON: команды могут писать туда и обычные
    /// пояснительные строки (например <c>removed incomplete file: …</c>).
    /// </summary>
    /// <param name="stderr">Полное содержимое stderr.</param>
    /// <returns>Разобранный JSON-документ ошибки.</returns>
    private static JsonDocument ParseErrorJson(string stderr)
    {
        var jsonLine = stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Last(l => l.StartsWith('{'));
        return JsonDocument.Parse(jsonLine);
    }

    /// <summary>
    /// Отмена посреди выполнения команды: exit 11, машиночитаемая ошибка на stderr,
    /// никакого стектрейса.
    /// </summary>
    [Test]
    public async Task Cancellation_MidCommand_ReturnsExit11_WithJsonError()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        using var cts = new CancellationTokenSource();
        env.InnerHandler = new TestHttpMessageHandler().Push(_ =>
        {
            // Отмена приходит ровно в тот момент, когда команда уже начала работу.
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "get", "DEV-1" }, sw, er, cts.Token);

        await Assert.That(exit).IsEqualTo(11);
        await Assert.That(sw.ToString()).IsEqualTo(string.Empty);

        using var doc = ParseErrorJson(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("cancelled");
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("message").GetString())
            .Contains("incomplete");

        // Стектрейса быть не должно ни в каком виде.
        await Assert.That(er.ToString()).DoesNotContain("   at ");
        await Assert.That(er.ToString()).DoesNotContain("OperationCanceledException");
        await Assert.That(er.ToString()).DoesNotContain("Unhandled exception");
    }

    /// <summary>
    /// Срабатывание HTTP-таймаута: тот же exit 11, но сообщение обязано назвать таймаут
    /// и способ его поднять — иначе отладка упирается в «просто оборвалось».
    /// </summary>
    [Test]
    public async Task Timeout_ReturnsExit11_AndNamesTimeoutAndHowToRaiseIt()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        env.InnerHandler = new NeverRespondingHandler();

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "issue", "get", "DEV-1", "--timeout", "1" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(11);

        using var doc = ParseErrorJson(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("cancelled");
        var message = doc.RootElement.GetProperty("error").GetProperty("message").GetString()!;
        await Assert.That(message).Contains("timed out after 1s");
        await Assert.That(message).Contains("--timeout");
        await Assert.That(message).Contains("YT_TIMEOUT");
        await Assert.That(er.ToString()).DoesNotContain("   at ");
    }

    /// <summary>
    /// Таймаут, заданный переменной окружения <c>YT_TIMEOUT</c>, попадает в текст
    /// сообщения так же, как значение флага.
    /// </summary>
    [Test]
    public async Task Timeout_FromEnvVariable_IsNamedInMessage()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        env.Set("YT_TIMEOUT", "1");
        env.InnerHandler = new NeverRespondingHandler();

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "get", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(11);
        using var doc = ParseErrorJson(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("message").GetString())
            .Contains("timed out after 1s");
    }

    /// <summary>
    /// Регрессия: обычная доменная ошибка по-прежнему отдаёт свой код из таблицы 2..10,
    /// а не новый код отмены.
    /// </summary>
    [Test]
    public async Task RegularTrackerError_StillReturnsItsOwnExitCode()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        env.InnerHandler = new TestHttpMessageHandler().Push(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"errorMessages":["not found"],"statusCode":404}"""),
            });

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "get", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(5);
        using var doc = ParseErrorJson(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("not_found");
    }

    /// <summary>
    /// Регрессия: успешная команда по-прежнему возвращает 0 и ничего не пишет в stderr.
    /// </summary>
    [Test]
    public async Task SuccessfulCommand_StillReturnsZero()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        env.InnerHandler = new TestHttpMessageHandler().Push(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"key":"DEV-1","summary":"ok"}"""),
            });

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "get", "DEV-1", "--format", "json" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(er.ToString()).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// Регрессия: некорректные аргументы разбираются до запуска команды и дают exit 2,
    /// обработчик отмены в это не вмешивается.
    /// </summary>
    [Test]
    public async Task InvalidArguments_StillReturnExitTwo()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        env.SetReadOnly(true);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "attachment", "download", "DEV-1", "42", "--out", "-", "--force" }, sw, er);

        await Assert.That(exit).IsEqualTo(2);
        using var doc = ParseErrorJson(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
    }

    /// <summary>
    /// Недопустимый <c>--timeout</c> отвергается явной ошибкой аргументов, а не превращается
    /// в исключение из глубины HTTP-конвейера.
    /// </summary>
    [Test]
    public async Task NonPositiveTimeout_IsRejectedAsInvalidArgs()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "get", "DEV-1", "--timeout", "0" }, sw, er);

        await Assert.That(exit).IsEqualTo(2);
        using var doc = ParseErrorJson(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("message").GetString())
            .Contains("--timeout");
    }

    /// <summary>
    /// То же для <c>YT_TIMEOUT</c>: мусорное значение переменной окружения не должно молча
    /// подменяться дефолтом — иначе пользователь считает, что поднял таймаут, а он не поднят.
    /// </summary>
    [Test]
    public async Task InvalidTimeoutEnvVariable_IsRejectedAsInvalidArgs()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        env.Set("YT_TIMEOUT", "-5");

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "issue", "get", "DEV-1" }, sw, er);

        await Assert.That(exit).IsEqualTo(2);
        using var doc = ParseErrorJson(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("message").GetString())
            .Contains("YT_TIMEOUT");
    }

    /// <summary>
    /// Сообщение об отмене не имеет права называть «текущим» значение, которого CLI
    /// никогда не применял: отвергнутый <c>YT_TIMEOUT</c> в тексте заменяется дефолтом.
    /// </summary>
    [Test]
    public async Task CancellationMessage_NeverQuotesRejectedTimeoutValue()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        env.Set("YT_TIMEOUT", "-5");

        var error = CancellationReport.ToException(
            CancellationCause.Unknown,
            signalName: null,
            TimeoutResolver.ResolveForMessage(null, EnvReader.Snapshot()));

        await Assert.That(error.Message).DoesNotContain("-5");
        await Assert.That(error.Message).Contains("currently 30s");
    }

    /// <summary>
    /// Классификация причины отмены. Маркер таймаута (<see cref="TimeoutException"/> внутри
    /// <see cref="TaskCanceledException"/>, как его ставит <c>HttpClient</c>) сильнее сигнала:
    /// таймаут, пришедший в момент Ctrl-C, всё равно остаётся таймаутом.
    /// </summary>
    [Test]
    public async Task Classify_TimeoutMarker_WinsOverSignal()
    {
        var ex = new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing.",
            new TimeoutException("A task was canceled."));

        var cause = CancellationReport.Classify(ex, signalName: "SIGINT", hostToken: CancellationToken.None);

        await Assert.That(cause).IsEqualTo(CancellationCause.Timeout);
    }

    /// <summary>
    /// Пришедший сигнал без маркера таймаута — отмена пользователем; сообщение называет
    /// сигнал и не упоминает <c>--timeout</c> как причину.
    /// </summary>
    [Test]
    public async Task Classify_Signal_IsUserInterrupt_AndMessageNamesIt()
    {
        var cause = CancellationReport.Classify(
            new OperationCanceledException(),
            signalName: "SIGINT",
            hostToken: CancellationToken.None);
        await Assert.That(cause).IsEqualTo(CancellationCause.Interrupted);

        var error = CancellationReport.ToException(cause, "SIGINT", timeoutSeconds: 30);
        await Assert.That(error.Code.ToExitCode()).IsEqualTo(11);
        await Assert.That(error.Message).Contains("SIGINT");
        await Assert.That(error.Message).Contains("incomplete");
    }

    /// <summary>
    /// Ни сигнала, ни маркера таймаута, ни отмены от вызывающего: причину назвать нельзя,
    /// поэтому сообщение остаётся общим и честно говорит об этом, но всё равно подсказывает,
    /// как поднять таймаут.
    /// </summary>
    [Test]
    public async Task Classify_NoEvidence_FallsBackToGenericMessage()
    {
        var cause = CancellationReport.Classify(
            new OperationCanceledException(),
            signalName: null,
            hostToken: CancellationToken.None);
        await Assert.That(cause).IsEqualTo(CancellationCause.Unknown);

        var error = CancellationReport.ToException(cause, null, timeoutSeconds: 30);
        await Assert.That(error.Message).Contains("could not be determined");
        await Assert.That(error.Message).Contains("YT_TIMEOUT");
    }

    /// <summary>
    /// Команда, которая не свернулась после сигнала, признаётся не отвечающей: exit 11
    /// с сообщением о принудительном завершении, а не бесконечное ожидание.
    /// </summary>
    [Test]
    public async Task Unresponsive_AfterSignal_IsReportedAsForcedTermination()
    {
        var error = CancellationReport.ToException(
            CancellationCause.Unresponsive,
            "SIGINT",
            timeoutSeconds: 30);

        await Assert.That(error.Code.ToExitCode()).IsEqualTo(11);
        await Assert.That(error.Code.ToWireName()).IsEqualTo("cancelled");
        await Assert.That(error.Message).Contains("SIGINT");
        await Assert.That(error.Message).Contains("did not stop in time");
    }

    /// <summary>
    /// Реальный путь «команда игнорирует отмену»: сигнал приходит, grace-период истекает,
    /// и <see cref="CliRunner"/> перестаёт ждать команду, отдавая exit 11. Проверяется
    /// именно ветвление в <c>Task.WhenAny</c>, а не форматирование сообщения.
    /// </summary>
    [Test]
    public async Task Unresponsive_RealPath_StopsWaiting_AndReturnsExit11()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        // Залипаем на чтении тела ответа: этот участок идёт уже после того, как HttpClient
        // отдал ответ, и его собственная отмена сюда не достаёт — команда действительно
        // не реагирует на токен, как и застрявшая в блокирующем syscall'е.
        var body = new UncancellableStream();
        env.InnerHandler = new TestHttpMessageHandler().Push(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(body),
            });

        var target = Path.Combine(Path.GetTempPath(), "yt-unresp-" + Guid.NewGuid().ToString("N"));
        var sw = new StringWriter();
        var er = new StringWriter();
        var started = Stopwatch.StartNew();
        var exit = await env.Invoke(
            new[] { "attachment", "download", "DEV-1", "42", "--out", target },
            sw,
            er,
            gracePeriod: TimeSpan.FromMilliseconds(50),
            onWatchReady: watch => SignalOnceStuck(watch, body.Stuck));
        started.Stop();

        await Assert.That(exit).IsEqualTo(11);

        // Ждать команду дальше нельзя: она не отвечает, а CLI обязан вернуть управление.
        await Assert.That(started.Elapsed).IsLessThan(UncancellableStream.HangFor);

        using var doc = ParseErrorJson(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("cancelled");
        var message = doc.RootElement.GetProperty("error").GetProperty("message").GetString()!;
        await Assert.That(message).Contains("did not stop in time");
        await Assert.That(message).Contains("SIGINT");
        await Assert.That(er.ToString()).DoesNotContain("   at ");
    }

    /// <summary>
    /// Маркер таймаута ищется на ограниченной глубине, и предел не обнуляется на каждом
    /// уровне <see cref="AggregateException"/>. Иначе патологически длинная цепочка уводит
    /// классификацию в рекурсию, которая заканчивается <c>StackOverflowException</c> —
    /// а он в .NET не перехватывается.
    /// </summary>
    [Test]
    public async Task Classify_DeeplyNestedAggregates_DoesNotRecurseWithoutLimit()
    {
        Exception chain = new TimeoutException("A task was canceled.");
        for (var i = 0; i < 64; i++)
        {
            chain = new AggregateException(chain);
        }

        var cause = CancellationReport.Classify(
            new OperationCanceledException("cancelled", chain),
            signalName: null,
            hostToken: CancellationToken.None);

        // Глубже предела маркер не ищется: причина остаётся неопределённой.
        await Assert.That(cause).IsEqualTo(CancellationCause.Unknown);
    }

    /// <summary>
    /// Регрессия к предыдущему тесту: неглубокая цепочка <see cref="AggregateException"/>
    /// по-прежнему распознаётся как таймаут — ограничение глубины не сломало распознавание.
    /// </summary>
    [Test]
    public async Task Classify_ShallowAggregate_StillDetectsTimeout()
    {
        var chain = new AggregateException(new AggregateException(new TimeoutException()));

        var cause = CancellationReport.Classify(
            new OperationCanceledException("cancelled", chain),
            signalName: null,
            hostToken: CancellationToken.None);

        await Assert.That(cause).IsEqualTo(CancellationCause.Timeout);
    }

    /// <summary>
    /// Подаёт «сигнал» обработчику после того, как команда действительно залипла: раньше
    /// отмена сработала бы штатно и путь «команда не отвечает» просто не был бы задействован.
    /// До вызова <see cref="InterruptWatch.Track"/> сигнал не подавляется, поэтому повторяем,
    /// пока не будет принят.
    /// </summary>
    /// <param name="watch">Обработчик сигналов запущенного вызова.</param>
    /// <param name="stuck">Сигнализирует, что команда вошла в неотменяемую операцию.</param>
    private static void SignalOnceStuck(InterruptWatch watch, Task stuck) =>
        _ = Task.Run(async () =>
        {
            await stuck;
            while (!watch.Handle(PosixSignal.SIGINT))
            {
                await Task.Delay(10);
            }
        });

    /// <summary>
    /// Поток тела ответа, который отдаёт префикс, а затем надолго залипает, игнорируя
    /// переданный токен: так ведёт себя команда, застрявшая в блокирующем syscall'е.
    /// </summary>
    private sealed class UncancellableStream : Stream
    {
        /// <summary>
        /// Сколько поток висит, не обращая внимания на отмену.
        /// </summary>
        public static readonly TimeSpan HangFor = TimeSpan.FromSeconds(5);

        private readonly TaskCompletionSource _stuck =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool _prefixSent;

        /// <summary>
        /// Завершается, когда поток вошёл в неотменяемое ожидание.
        /// </summary>
        public Task Stuck => _stuck.Task;

        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        /// <inheritdoc />
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!_prefixSent)
            {
                _prefixSent = true;
                buffer.Span[0] = 42;
                return 1;
            }

            _stuck.TrySetResult();
            await Task.Delay(HangFor, CancellationToken.None);

            // Обрываемся, а не отдаём EOF: иначе брошенная команда доскачивает файл уже
            // после теста и оставляет его в temp. С ошибкой она убирает свой временный файл.
            throw new IOException("abandoned");
        }
    }

    /// <summary>
    /// Handler, который никогда не отвечает и корректно реагирует на отмену:
    /// таймаут <c>HttpClient</c> проявляется на нём ровно так же, как в проде —
    /// <see cref="TaskCanceledException"/> с внутренним <see cref="TimeoutException"/>.
    /// </summary>
    private sealed class NeverRespondingHandler : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
