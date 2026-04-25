namespace YandexTrackerCLI.Tests.Output;

using TUnit.Core;
using YandexTrackerCLI.Output;

/// <summary>
/// Тесты <see cref="TerminalCapabilities.Detect"/>: резолвинг цветов, OSC 8 hyperlinks,
/// ширины и pager-настроек из env + CLI-флагов.
/// </summary>
public sealed class TerminalCapabilitiesTests
{
    private static IReadOnlyDictionary<string, string?> Env(params (string Key, string? Value)[] entries)
    {
        var d = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (k, v) in entries)
        {
            d[k] = v;
        }
        return d;
    }

    private static TerminalCapabilities Detect(
        IReadOnlyDictionary<string, string?> env,
        bool noColor = false,
        bool noPager = false,
        bool isOutputRedirected = false,
        int? consoleWidth = 100)
    {
        return TerminalCapabilities.Detect(
            env,
            noColorFlag: noColor,
            noPagerFlag: noPager,
            isOutputRedirected: () => isOutputRedirected,
            consoleWidth: () => consoleWidth);
    }

    [Test]
    public async Task NoColorEnv_DisablesColor()
    {
        var caps = Detect(Env(("NO_COLOR", "1")));
        await Assert.That(caps.UseColor).IsFalse();
    }

    [Test]
    public async Task NoColorFlag_DisablesColor()
    {
        var caps = Detect(Env(), noColor: true);
        await Assert.That(caps.UseColor).IsFalse();
    }

    [Test]
    public async Task TermDumb_DisablesColorAndHyperlinks()
    {
        var caps = Detect(Env(("TERM", "dumb"), ("TERM_PROGRAM", "iTerm.app")));
        await Assert.That(caps.UseColor).IsFalse();
        await Assert.That(caps.UseHyperlinks).IsFalse();
    }

    [Test]
    public async Task ITerm_EnablesHyperlinks_OnTty()
    {
        var caps = Detect(Env(("TERM_PROGRAM", "iTerm.app")));
        await Assert.That(caps.UseHyperlinks).IsTrue();
    }

    [Test]
    public async Task AppleTerminal_EnablesHyperlinks()
    {
        var caps = Detect(Env(("TERM_PROGRAM", "Apple_Terminal")));
        await Assert.That(caps.UseHyperlinks).IsTrue();
    }

    [Test]
    public async Task VsCode_EnablesHyperlinks()
    {
        var caps = Detect(Env(("TERM_PROGRAM", "vscode")));
        await Assert.That(caps.UseHyperlinks).IsTrue();
    }

    [Test]
    public async Task XtermPrefix_EnablesHyperlinks()
    {
        var caps = Detect(Env(("TERM", "xterm-256color")));
        await Assert.That(caps.UseHyperlinks).IsTrue();
    }

    [Test]
    public async Task ColorTermTruecolor_EnablesHyperlinks()
    {
        var caps = Detect(Env(("COLORTERM", "truecolor")));
        await Assert.That(caps.UseHyperlinks).IsTrue();
    }

    [Test]
    public async Task YtHyperlinks1_ForcesOn_EvenWithoutModernTerm()
    {
        var caps = Detect(Env(("YT_HYPERLINKS", "1")));
        await Assert.That(caps.UseHyperlinks).IsTrue();
    }

    [Test]
    public async Task YtHyperlinks0_ForcesOff_EvenOnModernTerm()
    {
        var caps = Detect(Env(("YT_HYPERLINKS", "0"), ("TERM_PROGRAM", "iTerm.app")));
        await Assert.That(caps.UseHyperlinks).IsFalse();
    }

    [Test]
    public async Task OutputRedirected_DisablesEverythingInteractive()
    {
        var caps = Detect(
            Env(("TERM_PROGRAM", "iTerm.app"), ("YT_PAGER", "less")),
            isOutputRedirected: true);

        await Assert.That(caps.UseColor).IsFalse();
        await Assert.That(caps.UseHyperlinks).IsFalse();
        await Assert.That(caps.UsePager).IsFalse();
    }

    [Test]
    public async Task TerminalWidth_FromEnv()
    {
        var caps = Detect(Env(("YT_TERMINAL_WIDTH", "120")), consoleWidth: 80);
        await Assert.That(caps.Width).IsEqualTo(120);
    }

    [Test]
    public async Task TerminalWidth_InvalidEnv_FallsBackToConsole()
    {
        var caps = Detect(Env(("YT_TERMINAL_WIDTH", "not-a-number")), consoleWidth: 90);
        await Assert.That(caps.Width).IsEqualTo(90);
    }

    [Test]
    public async Task TerminalWidth_NoConsole_UsesDefault()
    {
        var caps = Detect(Env(), consoleWidth: null);
        await Assert.That(caps.Width).IsEqualTo(TerminalCapabilities.DefaultWidth);
    }

    [Test]
    public async Task TerminalWidth_ClampsToMin()
    {
        var caps = Detect(Env(("YT_TERMINAL_WIDTH", "10")));
        await Assert.That(caps.Width).IsEqualTo(TerminalCapabilities.MinWidth);
    }

    [Test]
    public async Task TerminalWidth_ClampsToMax()
    {
        var caps = Detect(Env(("YT_TERMINAL_WIDTH", "999")));
        await Assert.That(caps.Width).IsEqualTo(TerminalCapabilities.MaxWidth);
    }

    [Test]
    public async Task PagerEmpty_DisablesPager()
    {
        var caps = Detect(Env(("YT_PAGER", "")));
        await Assert.That(caps.UsePager).IsFalse();
    }

    [Test]
    public async Task PagerCat_DisablesPager()
    {
        var caps = Detect(Env(("YT_PAGER", "cat")));
        await Assert.That(caps.UsePager).IsFalse();
    }

    [Test]
    public async Task NoPagerFlag_DisablesPager()
    {
        var caps = Detect(Env(("YT_PAGER", "less -R")), noPager: true);
        await Assert.That(caps.UsePager).IsFalse();
    }

    [Test]
    public async Task DefaultPager_WhenNoEnv_LessRfx()
    {
        var caps = Detect(Env());
        await Assert.That(caps.UsePager).IsTrue();
        await Assert.That(caps.PagerCommand).IsEqualTo("less -R -F -X");
    }

    [Test]
    public async Task SystemPagerEnv_UsedWhenYtPagerUnset()
    {
        var caps = Detect(Env(("PAGER", "more")));
        await Assert.That(caps.UsePager).IsTrue();
        await Assert.That(caps.PagerCommand).IsEqualTo("more");
    }

    [Test]
    public async Task YtPager_OverridesSystemPager()
    {
        var caps = Detect(Env(("PAGER", "more"), ("YT_PAGER", "less -R")));
        await Assert.That(caps.UsePager).IsTrue();
        await Assert.That(caps.PagerCommand).IsEqualTo("less -R");
    }

    [Test]
    public async Task DisabledStaticInstance_HasSafeDefaults()
    {
        var caps = TerminalCapabilities.Disabled;
        await Assert.That(caps.IsOutputRedirected).IsTrue();
        await Assert.That(caps.UseColor).IsFalse();
        await Assert.That(caps.UseHyperlinks).IsFalse();
        await Assert.That(caps.UsePager).IsFalse();
        await Assert.That(caps.Width).IsEqualTo(TerminalCapabilities.DefaultWidth);
    }
}
