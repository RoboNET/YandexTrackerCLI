namespace YandexTrackerCLI.Tests.Output;

using TUnit.Core;
using YandexTrackerCLI.Output;

/// <summary>
/// Тесты <see cref="PagerWriter"/>: главным образом проверяем поведение fallback'а,
/// потому что запуск настоящего pager-процесса в unit-тестах хрупкий и зависит от ОС.
/// </summary>
public sealed class PagerWriterTests
{
    private static TerminalCapabilities Caps(bool usePager, string pagerCommand = "less -R -F -X") =>
        new(
            IsOutputRedirected: false,
            UseColor: false,
            UseHyperlinks: false,
            Width: 80,
            UsePager: usePager,
            PagerCommand: pagerCommand);

    [Test]
    public async Task PagerOff_WritesDirectlyToFallback()
    {
        var sb = new StringWriter();
        using (var w = PagerWriter.Create(Caps(usePager: false), sb))
        {
            w.Write("hello");
            w.WriteLine(" world");
        }
        await Assert.That(sb.ToString().TrimEnd('\r', '\n')).IsEqualTo("hello world");
    }

    [Test]
    public async Task PagerOff_FallbackIsNotClosed_AfterDispose()
    {
        var sb = new StringWriter();
        using (var w = PagerWriter.Create(Caps(usePager: false), sb))
        {
            w.Write("first ");
        }
        // Должны иметь возможность писать в sb после Dispose.
        sb.Write("second");
        await Assert.That(sb.ToString()).IsEqualTo("first second");
    }

    [Test]
    public async Task PagerOn_InvalidCommand_FallsBackToDirect_AndWarns()
    {
        var sb = new StringWriter();
        var caps = Caps(usePager: true, pagerCommand: "no-such-pager-binary-zzz-9999");
        using (var w = PagerWriter.Create(caps, sb))
        {
            w.WriteLine("payload");
        }
        var output = sb.ToString();
        // Warning + payload оба попадают в fallback.
        await Assert.That(output).Contains("warning:");
        await Assert.That(output).Contains("payload");
    }
}
