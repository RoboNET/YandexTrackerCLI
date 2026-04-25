namespace YandexTrackerCLI.Tests.Output;

using TUnit.Core;
using YandexTrackerCLI.Output;

/// <summary>
/// Тесты <see cref="AnsiStyle"/> — обёртки в ANSI-коды и OSC 8 hyperlinks.
/// </summary>
public sealed class AnsiStyleTests
{
    private const string Esc = "";

    [Test]
    public async Task Bold_WithColor_WrapsInAnsi()
    {
        var actual = AnsiStyle.Bold("X", useColor: true);
        await Assert.That(actual).IsEqualTo($"{Esc}[1mX{Esc}[22m");
    }

    [Test]
    public async Task Bold_WithoutColor_ReturnsAsIs()
    {
        var actual = AnsiStyle.Bold("X", useColor: false);
        await Assert.That(actual).IsEqualTo("X");
    }

    [Test]
    public async Task Italic_WithColor_WrapsInAnsi()
    {
        var actual = AnsiStyle.Italic("hi", useColor: true);
        await Assert.That(actual).IsEqualTo($"{Esc}[3mhi{Esc}[23m");
    }

    [Test]
    public async Task Dim_WithColor_WrapsInAnsi()
    {
        var actual = AnsiStyle.Dim("note", useColor: true);
        await Assert.That(actual).IsEqualTo($"{Esc}[2mnote{Esc}[22m");
    }

    [Test]
    public async Task Underline_WithColor_WrapsInAnsi()
    {
        var actual = AnsiStyle.Underline("link", useColor: true);
        await Assert.That(actual).IsEqualTo($"{Esc}[4mlink{Esc}[24m");
    }

    [Test]
    public async Task CodeInline_WithColor_UsesReverseVideo()
    {
        var actual = AnsiStyle.CodeInline("foo", useColor: true);
        await Assert.That(actual).IsEqualTo($"{Esc}[7mfoo{Esc}[27m");
    }

    [Test]
    public async Task Hyperlink_WithSupport_WrapsInOsc8()
    {
        var actual = AnsiStyle.Hyperlink("https://x", "click", useHyperlinks: true);
        // ESC ] 8 ; ; URL ESC \ TEXT ESC ] 8 ; ; ESC \
        await Assert.That(actual).IsEqualTo($"{Esc}]8;;https://x{Esc}\\click{Esc}]8;;{Esc}\\");
    }

    [Test]
    public async Task Hyperlink_WithoutSupport_ReturnsTextOnly()
    {
        var actual = AnsiStyle.Hyperlink("https://x", "click", useHyperlinks: false);
        await Assert.That(actual).IsEqualTo("click");
    }

    [Test]
    public async Task VisibleLength_PlainText_CountsCharacters()
    {
        await Assert.That(AnsiStyle.VisibleLength("hello")).IsEqualTo(5);
    }

    [Test]
    public async Task VisibleLength_IgnoresCsiCodes()
    {
        var s = AnsiStyle.Bold("hello", useColor: true);
        await Assert.That(AnsiStyle.VisibleLength(s)).IsEqualTo(5);
    }

    [Test]
    public async Task VisibleLength_IgnoresOsc8Hyperlink()
    {
        var s = AnsiStyle.Hyperlink("https://example.com/long/path", "ABC", useHyperlinks: true);
        await Assert.That(AnsiStyle.VisibleLength(s)).IsEqualTo(3);
    }

    [Test]
    public async Task VisibleLength_StackedAttributes()
    {
        var s = AnsiStyle.Bold(AnsiStyle.Italic("X", useColor: true), useColor: true);
        await Assert.That(AnsiStyle.VisibleLength(s)).IsEqualTo(1);
    }

    [Test]
    public async Task VisibleLength_EmptyOrNull()
    {
        await Assert.That(AnsiStyle.VisibleLength(string.Empty)).IsEqualTo(0);
    }
}
