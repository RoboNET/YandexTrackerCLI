namespace YandexTrackerCLI.Tests.Output;

using TUnit.Core;
using YandexTrackerCLI.Output;

/// <summary>
/// Тесты <see cref="MarkdownTerminalRenderer"/>: парсинг блоков, inline-разметка,
/// word-wrap, OSC 8 hyperlinks и issue-key autolink.
/// </summary>
public sealed class MarkdownTerminalRendererTests
{
    private const string Esc = "";

    private static TerminalCapabilities Caps(
        bool color = true,
        bool hyper = true,
        int width = 80) =>
        new(
            IsOutputRedirected: false,
            UseColor: color,
            UseHyperlinks: hyper,
            Width: width,
            UsePager: false,
            PagerCommand: "less");

    [Test]
    public async Task EmptyMarkdown_ReturnsEmpty()
    {
        var actual = MarkdownTerminalRenderer.Render(string.Empty, Caps());
        await Assert.That(actual).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task PlainText_RenderedAsParagraph_WithWrap()
    {
        var input = "hello world from markdown";
        var actual = MarkdownTerminalRenderer.Render(input, Caps(color: false, hyper: false, width: 80));
        await Assert.That(actual.TrimEnd('\r', '\n')).IsEqualTo("hello world from markdown");
    }

    [Test]
    public async Task Heading_BoldedAndPlainText()
    {
        var actual = MarkdownTerminalRenderer.Render("# Title here", Caps(color: true, hyper: false));
        await Assert.That(actual).Contains($"{Esc}[1m");
        await Assert.That(actual).Contains("Title here");
        await Assert.That(actual).Contains($"{Esc}[22m");
    }

    [Test]
    public async Task BulletList_NoCheckbox_RendersDashPrefix()
    {
        var md = "- one\n- two";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: false, hyper: false));
        await Assert.That(actual).Contains("- one");
        await Assert.That(actual).Contains("- two");
    }

    [Test]
    public async Task BulletList_WithCheckbox_RendersBoxes()
    {
        var md = "- [ ] todo\n- [x] done";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: false, hyper: false));
        await Assert.That(actual).Contains("☐ todo"); // ☐ todo
        await Assert.That(actual).Contains("☑ done"); // ☑ done
    }

    [Test]
    public async Task Checkbox_WithoutBulletPrefix_StillParsedAsChecklist()
    {
        // Tracker и многие редакторы эмитят чек-листы без ведущего bullet-marker.
        var md = "[ ] Bfs.Terminal\n[ ] Bfs.Identity\n[x] Bfs.Done";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: false, hyper: false));
        await Assert.That(actual).Contains("☐ Bfs.Terminal");
        await Assert.That(actual).Contains("☐ Bfs.Identity");
        await Assert.That(actual).Contains("☑ Bfs.Done");
        // НЕ должно быть литеральных квадратных скобок [ ] / [x] в выводе.
        await Assert.That(actual.Contains("[ ]")).IsFalse();
        await Assert.That(actual.Contains("[x]")).IsFalse();
    }

    [Test]
    public async Task SoftBreak_SingleNewline_TreatedAsHardLineBreak()
    {
        // Tracker (как GFM/Slack/Notion) трактует одиночный \n как hard break:
        // каждая строка идёт на собственной физической строке.
        var md = "line1\nline2";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: false, hyper: false, width: 80));
        var lines = actual.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        await Assert.That(lines.Length).IsEqualTo(2);
        await Assert.That(lines[0].TrimEnd('\r')).IsEqualTo("line1");
        await Assert.That(lines[1].TrimEnd('\r')).IsEqualTo("line2");
        // Не должно склеиваться в "line1 line2".
        await Assert.That(actual.Contains("line1 line2")).IsFalse();
    }

    [Test]
    public async Task SoftBreak_TrackerStyleUrls_PreservedOnSeparateLines()
    {
        // Реальный кейс: список URL'ов в одном параграфе.
        const string md = "Пулл реквесты в дев:\nhttps://gitlab.example/repo/-/merge_requests/835\nhttps://gitlab.example/repo/-/merge_requests/787";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: false, hyper: false, width: 200));
        var lines = actual.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        await Assert.That(lines.Length).IsEqualTo(3);
        await Assert.That(lines[0].TrimEnd('\r')).IsEqualTo("Пулл реквесты в дев:");
        await Assert.That(lines[1].TrimEnd('\r')).Contains("835");
        await Assert.That(lines[2].TrimEnd('\r')).Contains("787");
    }

    [Test]
    public async Task SoftBreak_DoubleNewline_StartsNewParagraph()
    {
        // Двойной \n продолжает разделять блоки (между ними blank line).
        var md = "first\n\nsecond";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: false, hyper: false, width: 80));
        await Assert.That(actual).Contains("first");
        await Assert.That(actual).Contains("second");
        // Между параграфами должна быть пустая строка.
        var lines = actual.Split('\n').Select(s => s.TrimEnd('\r')).ToArray();
        var firstIdx = Array.IndexOf(lines, "first");
        var secondIdx = Array.IndexOf(lines, "second");
        await Assert.That(firstIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(secondIdx).IsGreaterThan(firstIdx + 1);
    }

    [Test]
    public async Task NumberedList_PreservesOriginalNumbering()
    {
        var md = "3. third\n4. fourth";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: false, hyper: false));
        await Assert.That(actual).Contains("3. third");
        await Assert.That(actual).Contains("4. fourth");
    }

    [Test]
    public async Task FencedCodeBlock_RendersDimAndIndented()
    {
        var md = "```bash\necho hello\n```";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: true, hyper: false));
        await Assert.That(actual).Contains($"{Esc}[2mecho hello{Esc}[22m");
    }

    [Test]
    public async Task FencedCodeBlock_NoColorRendersAsPlain()
    {
        var md = "```\nlet x = 1\n```";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: false, hyper: false));
        await Assert.That(actual).Contains("let x = 1");
        await Assert.That(actual.Contains(Esc)).IsFalse();
    }

    [Test]
    public async Task Bold_AndItalic_Combined()
    {
        var md = "**hello** *world*";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: true, hyper: false));
        await Assert.That(actual).Contains($"{Esc}[1mhello{Esc}[22m");
        await Assert.That(actual).Contains($"{Esc}[3mworld{Esc}[23m");
    }

    [Test]
    public async Task InlineCode_UsesReverseVideo()
    {
        var md = "use `git status`";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: true, hyper: false));
        await Assert.That(actual).Contains($"{Esc}[7mgit status{Esc}[27m");
    }

    [Test]
    public async Task Link_WithHyperlinks_EmitsOsc8()
    {
        var md = "[click](https://example.com)";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: false, hyper: true));
        await Assert.That(actual).Contains($"{Esc}]8;;https://example.com{Esc}\\");
        await Assert.That(actual).Contains("click");
    }

    [Test]
    public async Task Link_WithoutHyperlinks_AppendsUrlInParens()
    {
        var md = "[click](https://example.com)";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: false, hyper: false));
        await Assert.That(actual).Contains("click (https://example.com)");
    }

    [Test]
    public async Task ImageReference_RendersAttachmentMarkerAndOsc8()
    {
        var md = "![diagram](https://example.com/diag.png)";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: false, hyper: true));
        await Assert.That(actual).Contains("[📎 diagram]");
        await Assert.That(actual).Contains($"{Esc}]8;;https://example.com/diag.png{Esc}\\");
    }

    [Test]
    public async Task ImageReference_RelativePath_PrependsTrackerHost()
    {
        var md = "![pic](/ajax/v2/attachments/123)";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: false, hyper: true));
        await Assert.That(actual).Contains("https://tracker.yandex.ru/ajax/v2/attachments/123");
    }

    [Test]
    public async Task IssueKey_AutoLinks_ToTrackerUrl_WithHyperlinks()
    {
        var md = "see TECH-123 for details";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: false, hyper: true));
        await Assert.That(actual).Contains("TECH-123");
        await Assert.That(actual).Contains("https://tracker.yandex.ru/TECH-123");
    }

    [Test]
    public async Task IssueKey_NoHyperlinks_OnlyUnderlines()
    {
        var md = "see DEV-1 here";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: true, hyper: false));
        await Assert.That(actual).Contains($"{Esc}[4mDEV-1{Esc}[24m");
        await Assert.That(actual.Contains($"{Esc}]8;;")).IsFalse(); // no OSC 8 marker.
    }

    [Test]
    public async Task AutoLink_BareUrl_StyledAsLink()
    {
        var md = "go to https://yandex.ru/help";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: false, hyper: true));
        await Assert.That(actual).Contains("https://yandex.ru/help");
    }

    [Test]
    public async Task Blockquote_PrefixedWithBar()
    {
        var md = "> wisdom from above";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: false, hyper: false));
        await Assert.That(actual).Contains("│ wisdom from above");
    }

    [Test]
    public async Task HorizontalRule_RendersDashes()
    {
        var actual = MarkdownTerminalRenderer.Render("---", Caps(color: false, hyper: false, width: 20));
        await Assert.That(actual).Contains(new string('─', 20));
    }

    [Test]
    public async Task WordWrap_RespectsWidth_WithoutBreakingAnsi()
    {
        var md = "**hello** world this paragraph is intentionally long and must wrap";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: true, hyper: false, width: 30));
        var lines = actual.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            await Assert.That(AnsiStyle.VisibleLength(trimmed)).IsLessThanOrEqualTo(30);
        }
        await Assert.That(actual).Contains($"{Esc}[1mhello{Esc}[22m");
    }

    [Test]
    public async Task WordWrap_AnsiSequenceCountsAsZero()
    {
        var styled = AnsiStyle.Bold("hello", useColor: true) + " world";
        var lines = MarkdownTerminalRenderer.WordWrap(styled, 11);
        await Assert.That(lines.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Strikethrough_RendersWithAnsi()
    {
        var actual = MarkdownTerminalRenderer.Render("~~old~~", Caps(color: true, hyper: false));
        await Assert.That(actual).Contains($"{Esc}[9mold{Esc}[29m");
    }

    [Test]
    public async Task Strikethrough_NoColor_PlainText()
    {
        var actual = MarkdownTerminalRenderer.Render("~~old~~", Caps(color: false, hyper: false));
        await Assert.That(actual).Contains("old");
        await Assert.That(actual.Contains(Esc)).IsFalse();
        await Assert.That(actual.Contains("~~")).IsFalse();
    }

    [Test]
    public async Task ColorTag_KnownColor_Red_RendersAnsi()
    {
        var actual = MarkdownTerminalRenderer.Render("{red}(err)", Caps(color: true, hyper: false));
        await Assert.That(actual).Contains($"{Esc}[31merr{Esc}[39m");
    }

    [Test]
    public async Task ColorTag_KnownColor_Gray_RendersAnsi()
    {
        var actual = MarkdownTerminalRenderer.Render("{gray}(note)", Caps(color: true, hyper: false));
        await Assert.That(actual).Contains($"{Esc}[90mnote{Esc}[39m");
    }

    [Test]
    public async Task ColorTag_KnownColor_Orange_RendersAnsi256()
    {
        var actual = MarkdownTerminalRenderer.Render("{orange}(warn)", Caps(color: true, hyper: false));
        await Assert.That(actual).Contains($"{Esc}[38;5;208mwarn{Esc}[39m");
    }

    [Test]
    public async Task ColorTag_GreyAlias_SameAsGray()
    {
        var actual = MarkdownTerminalRenderer.Render("{grey}(note)", Caps(color: true, hyper: false));
        await Assert.That(actual).Contains($"{Esc}[90mnote{Esc}[39m");
    }

    [Test]
    public async Task ColorTag_PurpleAlias_RendersMagenta()
    {
        var actual = MarkdownTerminalRenderer.Render("{purple}(p)", Caps(color: true, hyper: false));
        await Assert.That(actual).Contains($"{Esc}[35mp{Esc}[39m");
    }

    [Test]
    public async Task ColorTag_UnknownColor_RendersPlain()
    {
        var actual = MarkdownTerminalRenderer.Render("{xyz}(text)", Caps(color: true, hyper: false));
        await Assert.That(actual).Contains("text");
        // No ANSI markup, no leftover {xyz}(...) syntax.
        await Assert.That(actual.Contains("{xyz}")).IsFalse();
        await Assert.That(actual.Contains("(text)")).IsFalse();
    }

    [Test]
    public async Task ColorTag_NoColor_RendersPlain()
    {
        var actual = MarkdownTerminalRenderer.Render("{red}(err)", Caps(color: false, hyper: false));
        await Assert.That(actual).Contains("err");
        await Assert.That(actual.Contains(Esc)).IsFalse();
        await Assert.That(actual.Contains("{red}")).IsFalse();
    }

    [Test]
    public async Task EscapedParens_RendersLiteral()
    {
        var actual = MarkdownTerminalRenderer.Render("\\(text\\)", Caps(color: false, hyper: false));
        await Assert.That(actual).Contains("(text)");
        // Backslash-character не должен утечь в вывод.
        await Assert.That(actual.Contains("\\(")).IsFalse();
        await Assert.That(actual.Contains("\\)")).IsFalse();
    }

    [Test]
    public async Task EscapedAsterisk_PreventsBold()
    {
        var actual = MarkdownTerminalRenderer.Render("\\*not bold\\*", Caps(color: true, hyper: false));
        await Assert.That(actual).Contains("*not bold*");
        // Не должно быть ANSI bold (1m).
        await Assert.That(actual.Contains($"{Esc}[1m")).IsFalse();
    }

    [Test]
    public async Task EscapedBackslash_RendersLiteral()
    {
        var actual = MarkdownTerminalRenderer.Render("\\\\", Caps(color: false, hyper: false));
        // \\ → литеральный \
        await Assert.That(actual).Contains("\\");
    }

    [Test]
    public async Task UnescapedBackslash_PreservedAsIs()
    {
        // \t и \f не входят в EscapableChars — backslash сохраняется как есть.
        var actual = MarkdownTerminalRenderer.Render("path\\to\\file", Caps(color: false, hyper: false));
        await Assert.That(actual).Contains("path\\to\\file");
    }

    [Test]
    public async Task Combined_StrikethroughOverColor_RealTrackerExample()
    {
        // Реальный пример из Tracker'а: зачёркнутый серый текст.
        const string md = "~~{gray}(должно быть реализовано)~~";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: true, hyper: false));
        // Ожидаем strikethrough wrapper + color wrapper внутри.
        await Assert.That(actual).Contains($"{Esc}[9m");   // strikethrough open
        await Assert.That(actual).Contains($"{Esc}[29m");  // strikethrough close
        await Assert.That(actual).Contains($"{Esc}[90m");  // gray open
        await Assert.That(actual).Contains($"{Esc}[39m");  // gray close
        await Assert.That(actual).Contains("должно быть реализовано");
    }

    [Test]
    public async Task ColorTag_WithEscapedParensInside_RendersLiteralParens()
    {
        // {orange}(\(text\)) → orange-coloured "(text)"
        var actual = MarkdownTerminalRenderer.Render("{orange}(\\(text\\))", Caps(color: true, hyper: false));
        await Assert.That(actual).Contains($"{Esc}[38;5;208m");
        await Assert.That(actual).Contains("(text)");
        await Assert.That(actual).Contains($"{Esc}[39m");
    }

    [Test]
    public async Task LeftIndent_AppliedToAllLines()
    {
        var md = "first paragraph\n\nsecond paragraph";
        var actual = MarkdownTerminalRenderer.Render(md, Caps(color: false, hyper: false), leftIndent: 4);
        var lines = actual.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length > 0)
            {
                await Assert.That(trimmed).StartsWith("    ");
            }
        }
    }
}
