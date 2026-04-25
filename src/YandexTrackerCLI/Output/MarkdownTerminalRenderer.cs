namespace YandexTrackerCLI.Output;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Минималистичный markdown-to-ANSI рендерер для CLI: парсит подмножество
/// CommonMark (headings, paragraphs, bullet/numbered lists, checkboxes, fenced
/// code blocks, blockquotes, horizontal rules, inline code/bold/italic/links/
/// images/auto-links/issue keys) и форматирует под выбранный <see cref="TerminalCapabilities"/>.
/// </summary>
/// <remarks>
/// AOT-safe: использует только обычный <see cref="Regex"/> (не source-generators), <see cref="StringBuilder"/>
/// и работу со строками.
/// </remarks>
public static class MarkdownTerminalRenderer
{
    /// <summary>
    /// Базовый URL Yandex Tracker для разрешения относительных ссылок (например, attachment-paths).
    /// </summary>
    private const string TrackerBaseUrl = "https://tracker.yandex.ru";

    private static readonly Regex HeadingRegex =
        new("^(#{1,6})\\s+(.+)$", RegexOptions.Compiled);

    private static readonly Regex HrRegex =
        new("^[-*_]{3,}\\s*$", RegexOptions.Compiled);

    private static readonly Regex FencedOpenRegex =
        new("^```(.*)$", RegexOptions.Compiled);

    // Bullet-prefix перед чекбоксом опционален (Tracker и многие markdown-редакторы
    // эмитят чек-листы как просто `[ ] item`/`[x] item` без `-`/`*`/`+`).
    private static readonly Regex BulletCheckboxRegex =
        new("^(\\s*)(?:[-*+]\\s+)?\\[([ xX])\\]\\s+(.+)$", RegexOptions.Compiled);

    private static readonly Regex BulletRegex =
        new("^(\\s*)[-*+]\\s+(.+)$", RegexOptions.Compiled);

    private static readonly Regex NumberedRegex =
        new("^(\\s*)(\\d+)[\\.)]\\s+(.+)$", RegexOptions.Compiled);

    private static readonly Regex BlockquoteRegex =
        new("^>\\s?(.*)$", RegexOptions.Compiled);

    /// <summary>
    /// Рендерит markdown в строку с ANSI-разметкой и word-wrap.
    /// </summary>
    /// <param name="markdown">Исходный markdown-текст.</param>
    /// <param name="caps">Возможности терминала (color/hyperlinks/width).</param>
    /// <param name="leftIndent">Дополнительный отступ слева (пробелов) для всех строк
    /// результата. Используется detail view с indent=2.</param>
    /// <returns>Готовая строка для печати в TextWriter.</returns>
    public static string Render(string markdown, TerminalCapabilities caps, int leftIndent = 0)
    {
        var blocks = ParseBlocks(markdown);
        var sb = new StringBuilder();
        var indent = new string(' ', leftIndent);

        var writeBlankBefore = false;
        foreach (var block in blocks)
        {
            if (writeBlankBefore)
            {
                sb.AppendLine();
            }
            writeBlankBefore = true;
            RenderBlock(sb, block, caps, indent);
        }

        return sb.ToString();
    }

    private abstract record Block;

    private sealed record HeadingBlock(int Level, string Text) : Block;

    /// <summary>
    /// Параграф = последовательность отдельных строк. Tracker (как GFM/Slack/Notion)
    /// трактует одиночный <c>\n</c> как hard line break — каждая строка идёт на
    /// собственной физической строке. Word-wrap применяется только при overflow
    /// внутри одной строки.
    /// </summary>
    private sealed record ParagraphBlock(List<string> Lines) : Block;

    private sealed record BulletListBlock(List<ListItem> Items) : Block;

    private sealed record NumberedListBlock(List<ListItem> Items) : Block;

    private sealed record CodeBlock(string? Language, List<string> Lines) : Block;

    private sealed record BlockquoteBlock(string Text) : Block;

    private sealed record HorizontalRuleBlock : Block;

    private sealed record ListItem(string Text, bool? Checked, string? Marker);

    private static List<Block> ParseBlocks(string markdown)
    {
        var blocks = new List<Block>();
        // Normalize newlines.
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        ParagraphBuffer? paragraph = null;
        BulletListBuffer? bulletList = null;
        NumberedListBuffer? numberedList = null;
        BlockquoteBuffer? blockquote = null;

        void FlushParagraph()
        {
            if (paragraph is { Lines.Count: > 0 } p)
            {
                // Каждая строка параграфа сохраняется отдельно (hard break семантика).
                blocks.Add(new ParagraphBlock(new List<string>(p.Lines)));
            }
            paragraph = null;
        }

        void FlushBullet()
        {
            if (bulletList is { Items.Count: > 0 } b)
            {
                blocks.Add(new BulletListBlock(b.Items));
            }
            bulletList = null;
        }

        void FlushNumbered()
        {
            if (numberedList is { Items.Count: > 0 } n)
            {
                blocks.Add(new NumberedListBlock(n.Items));
            }
            numberedList = null;
        }

        void FlushBlockquote()
        {
            if (blockquote is { Lines.Count: > 0 } q)
            {
                blocks.Add(new BlockquoteBlock(string.Join(" ", q.Lines)));
            }
            blockquote = null;
        }

        void FlushAll()
        {
            FlushParagraph();
            FlushBullet();
            FlushNumbered();
            FlushBlockquote();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Empty line — terminate current block.
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushAll();
                continue;
            }

            // Fenced code block.
            var fence = FencedOpenRegex.Match(line);
            if (fence.Success)
            {
                FlushAll();
                var lang = fence.Groups[1].Value.Trim();
                var codeLines = new List<string>();
                i++;
                while (i < lines.Length)
                {
                    if (lines[i].StartsWith("```", StringComparison.Ordinal))
                    {
                        break;
                    }
                    codeLines.Add(lines[i]);
                    i++;
                }
                blocks.Add(new CodeBlock(string.IsNullOrEmpty(lang) ? null : lang, codeLines));
                continue;
            }

            if (HrRegex.IsMatch(line))
            {
                FlushAll();
                blocks.Add(new HorizontalRuleBlock());
                continue;
            }

            var heading = HeadingRegex.Match(line);
            if (heading.Success)
            {
                FlushAll();
                var level = heading.Groups[1].Value.Length;
                var text = heading.Groups[2].Value.Trim();
                blocks.Add(new HeadingBlock(level, text));
                continue;
            }

            var checkbox = BulletCheckboxRegex.Match(line);
            if (checkbox.Success)
            {
                FlushParagraph();
                FlushNumbered();
                FlushBlockquote();
                bulletList ??= new BulletListBuffer();
                var marker = checkbox.Groups[2].Value;
                var isChecked = string.Equals(marker, "x", StringComparison.OrdinalIgnoreCase);
                bulletList.Items.Add(new ListItem(checkbox.Groups[3].Value.Trim(), isChecked, null));
                continue;
            }

            var bullet = BulletRegex.Match(line);
            if (bullet.Success)
            {
                FlushParagraph();
                FlushNumbered();
                FlushBlockquote();
                bulletList ??= new BulletListBuffer();
                bulletList.Items.Add(new ListItem(bullet.Groups[2].Value.Trim(), null, null));
                continue;
            }

            var numbered = NumberedRegex.Match(line);
            if (numbered.Success)
            {
                FlushParagraph();
                FlushBullet();
                FlushBlockquote();
                numberedList ??= new NumberedListBuffer();
                var marker = numbered.Groups[2].Value;
                numberedList.Items.Add(new ListItem(numbered.Groups[3].Value.Trim(), null, marker));
                continue;
            }

            var quote = BlockquoteRegex.Match(line);
            if (quote.Success)
            {
                FlushParagraph();
                FlushBullet();
                FlushNumbered();
                blockquote ??= new BlockquoteBuffer();
                blockquote.Lines.Add(quote.Groups[1].Value.Trim());
                continue;
            }

            // Default → paragraph (soft-break between consecutive lines).
            FlushBullet();
            FlushNumbered();
            FlushBlockquote();
            paragraph ??= new ParagraphBuffer();
            paragraph.Lines.Add(line.Trim());
        }

        FlushAll();
        return blocks;
    }

    private sealed class ParagraphBuffer
    {
        public List<string> Lines { get; } = new();
    }

    private sealed class BulletListBuffer
    {
        public List<ListItem> Items { get; } = new();
    }

    private sealed class NumberedListBuffer
    {
        public List<ListItem> Items { get; } = new();
    }

    private sealed class BlockquoteBuffer
    {
        public List<string> Lines { get; } = new();
    }

    private static void RenderBlock(StringBuilder sb, Block block, TerminalCapabilities caps, string indent)
    {
        switch (block)
        {
            case HeadingBlock h:
                RenderHeading(sb, h, caps, indent);
                break;
            case ParagraphBlock p:
                RenderParagraph(sb, p, caps, indent);
                break;
            case BulletListBlock bl:
                RenderBulletList(sb, bl, caps, indent);
                break;
            case NumberedListBlock nl:
                RenderNumberedList(sb, nl, caps, indent);
                break;
            case CodeBlock cb:
                RenderCodeBlock(sb, cb, caps, indent);
                break;
            case BlockquoteBlock bq:
                RenderBlockquote(sb, bq, caps, indent);
                break;
            case HorizontalRuleBlock:
                RenderHorizontalRule(sb, caps, indent);
                break;
        }
    }

    private static void RenderHeading(StringBuilder sb, HeadingBlock h, TerminalCapabilities caps, string indent)
    {
        var text = RenderInline(h.Text, caps);
        var styled = AnsiStyle.Bold(text, caps.UseColor);
        sb.Append(indent);
        sb.AppendLine(styled);
    }

    private static void RenderParagraph(StringBuilder sb, ParagraphBlock p, TerminalCapabilities caps, string indent)
    {
        var width = Math.Max(20, caps.Width - indent.Length);
        // Каждая исходная строка → отдельная физическая строка (Tracker hard-break).
        // Word-wrap применяется только если строка длиннее `width`.
        foreach (var rawLine in p.Lines)
        {
            var rendered = RenderInline(rawLine, caps);
            var wrapped = WordWrap(rendered, width);
            foreach (var line in wrapped)
            {
                sb.Append(indent);
                sb.AppendLine(line);
            }
        }
    }

    private static void RenderBulletList(StringBuilder sb, BulletListBlock list, TerminalCapabilities caps, string indent)
    {
        foreach (var item in list.Items)
        {
            // Unicode box characters лучше передают checkbox-семантику и не путаются с
            // литеральными скобками в текстах. Plain bullet остаётся ASCII '-' для совместимости
            // с pipe в текстовые редакторы.
            var prefix = item.Checked switch
            {
                true  => "☑ ",
                false => "☐ ",
                null  => "- ",
            };
            var rendered = RenderInline(item.Text, caps);
            var visiblePrefixLen = AnsiStyle.VisibleLength(prefix);
            var width = Math.Max(10, caps.Width - indent.Length - visiblePrefixLen);
            var wrapped = WordWrap(rendered, width);
            for (var i = 0; i < wrapped.Count; i++)
            {
                sb.Append(indent);
                sb.Append(i == 0 ? prefix : new string(' ', visiblePrefixLen));
                sb.AppendLine(wrapped[i]);
            }
        }
    }

    private static void RenderNumberedList(StringBuilder sb, NumberedListBlock list, TerminalCapabilities caps, string indent)
    {
        foreach (var item in list.Items)
        {
            var marker = item.Marker ?? "1";
            var prefix = marker + ". ";
            var rendered = RenderInline(item.Text, caps);
            var width = Math.Max(10, caps.Width - indent.Length - prefix.Length);
            var wrapped = WordWrap(rendered, width);
            for (var i = 0; i < wrapped.Count; i++)
            {
                sb.Append(indent);
                sb.Append(i == 0 ? prefix : new string(' ', prefix.Length));
                sb.AppendLine(wrapped[i]);
            }
        }
    }

    private static void RenderCodeBlock(StringBuilder sb, CodeBlock block, TerminalCapabilities caps, string indent)
    {
        var codePrefix = indent + "  ";
        foreach (var raw in block.Lines)
        {
            // Code is preserved literally (no inline parsing, no wrap).
            var content = AnsiStyle.Dim(raw, caps.UseColor);
            sb.Append(codePrefix);
            sb.AppendLine(content);
        }
    }

    private static void RenderBlockquote(StringBuilder sb, BlockquoteBlock quote, TerminalCapabilities caps, string indent)
    {
        var marker = AnsiStyle.Dim("│ ", caps.UseColor);
        var visibleMarkerLen = AnsiStyle.VisibleLength(marker);
        var width = Math.Max(10, caps.Width - indent.Length - visibleMarkerLen);
        var rendered = RenderInline(quote.Text, caps);
        var lines = WordWrap(rendered, width);
        foreach (var line in lines)
        {
            sb.Append(indent);
            sb.Append(marker);
            sb.AppendLine(AnsiStyle.Italic(line, caps.UseColor));
        }
    }

    private static void RenderHorizontalRule(StringBuilder sb, TerminalCapabilities caps, string indent)
    {
        var width = Math.Max(4, caps.Width - indent.Length);
        sb.Append(indent);
        sb.AppendLine(new string('─', width));
    }

    // -------- Inline parser --------

    private static readonly Regex InlineCodeRegex =
        new("`([^`]+)`", RegexOptions.Compiled);

    private static readonly Regex ImageRegex =
        new("!\\[([^\\]]*)\\]\\(([^\\)]+)\\)", RegexOptions.Compiled);

    private static readonly Regex LinkRegex =
        new("\\[([^\\]]+)\\]\\(([^\\)]+)\\)", RegexOptions.Compiled);

    private static readonly Regex BoldRegex =
        new("\\*\\*([^*]+)\\*\\*", RegexOptions.Compiled);

    private static readonly Regex BoldUnderscoreRegex =
        new("__([^_]+)__", RegexOptions.Compiled);

    private static readonly Regex ItalicRegex =
        new("(?<![*])\\*(?!\\*)([^*\\n]+?)\\*(?![*])", RegexOptions.Compiled);

    private static readonly Regex ItalicUnderscoreRegex =
        new("(?<![_A-Za-z0-9])_(?!_)([^_\\n]+?)_(?![_A-Za-z0-9])", RegexOptions.Compiled);

    private static readonly Regex AutoLinkRegex =
        new("https?://[^\\s\\)\\]]+", RegexOptions.Compiled);

    private static readonly Regex IssueKeyRegex =
        new("\\b[A-Z][A-Z0-9]+-\\d+\\b", RegexOptions.Compiled);

    /// <summary>
    /// Strikethrough (GFM): <c>~~text~~</c>. Lazy match чтобы не «съедать» соседние пары.
    /// </summary>
    private static readonly Regex StrikethroughRegex =
        new("~~(.+?)~~", RegexOptions.Compiled);

    /// <summary>
    /// Tracker color tag: <c>{name}(text)</c>, где <c>name</c> — алфавитное имя цвета.
    /// Внутри скобок допустимы любые символы кроме литеральных <c>(</c>/<c>)</c> —
    /// escape-последовательности <c>\(</c>/<c>\)</c> уже подменены placeholder'ами
    /// на этом этапе.
    /// </summary>
    private static readonly Regex ColorTagRegex =
        new("\\{([a-zA-Z]+)\\}\\(([^()]+)\\)", RegexOptions.Compiled);

    /// <summary>
    /// Тип маркера, используемый для предотвращения повторной обработки уже
    /// рендеренных фрагментов inline-парсером.
    /// </summary>
    private const char MarkerOpen = '';
    private const char MarkerClose = '';

    /// <summary>
    /// Множество escape-able спец-символов markdown'а. Backslash перед любым из них
    /// заменяется на литерал, перед остальными — backslash сохраняется как есть.
    /// </summary>
    private static readonly HashSet<char> EscapableChars = new()
    {
        '(', ')', '[', ']', '*', '_', '~', '`', '\\', '!', '#', '{', '}',
    };

    /// <summary>
    /// Применяет inline-парсинг к тексту и возвращает строку с ANSI-разметкой.
    /// </summary>
    /// <remarks>
    /// Порядок шагов: inline-code → escape sequences → strikethrough → color tags →
    /// images → links → bold → italic → autolink → issue keys. Escape sequences
    /// идут вторыми (после inline-code) чтобы все последующие парсеры видели
    /// placeholder'ы вместо литералов <c>\(</c>, <c>\*</c> и т.п.
    /// </remarks>
    private static string RenderInline(string text, TerminalCapabilities caps)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var placeholders = new List<string>();

        string Reserve(string rendered)
        {
            placeholders.Add(rendered);
            return MarkerOpen + (placeholders.Count - 1).ToString(CultureInfo.InvariantCulture) + MarkerClose;
        }

        // 1) Inline code — никакой дальнейший парсинг внутри.
        var withCode = InlineCodeRegex.Replace(text, m =>
        {
            var content = m.Groups[1].Value;
            return Reserve(AnsiStyle.CodeInline(content, caps.UseColor));
        });

        // 2) Backslash escape sequences: \( \) \[ \] \* \_ \~ \` \\ \! \# \{ \}.
        // Каждая такая последовательность подменяется placeholder'ом, содержащим
        // только литерал — это надёжно «прячет» спец-символ от всех последующих
        // regex-парсеров. Backslash перед произвольным символом сохраняется как есть.
        var withEscapes = ApplyEscapes(withCode, Reserve);

        // 3) Color tags {name}(text). Применяются ДО strikethrough чтобы корректно
        // обрабатывать вложенный кейс ~~{color}(text)~~ — strikethrough получит
        // на вход уже placeholder и обернёт его. Неизвестные имена → text без markup.
        var withColors = ColorTagRegex.Replace(withEscapes, m =>
        {
            var name = m.Groups[1].Value;
            var inner = m.Groups[2].Value;
            var sgr = ResolveColorSgr(name);
            if (sgr is null)
            {
                // Неизвестный цвет → выводим только текст без markup-меток.
                return Reserve(inner);
            }
            return Reserve(AnsiStyle.ForegroundSgr(sgr, inner, caps.UseColor));
        });

        // 4) Strikethrough (~~text~~) — оборачивает уже-зарезервированные color-теги.
        var withStrike = StrikethroughRegex.Replace(withColors, m =>
        {
            var inner = m.Groups[1].Value;
            return Reserve(AnsiStyle.Strikethrough(inner, caps.UseColor));
        });

        // 5) Images — могут содержать URL, обрабатываем до обычных ссылок.
        var withImages = ImageRegex.Replace(withStrike, m =>
        {
            var alt = m.Groups[1].Value;
            var url = ResolveUrl(m.Groups[2].Value);
            var label = "[📎 " + (string.IsNullOrEmpty(alt) ? "image" : alt) + "]";
            var styled = AnsiStyle.Hyperlink(url, label, caps.UseHyperlinks);
            return Reserve(styled);
        });

        // 6) Links.
        var withLinks = LinkRegex.Replace(withImages, m =>
        {
            var label = m.Groups[1].Value;
            var url = ResolveUrl(m.Groups[2].Value);
            string rendered;
            if (caps.UseHyperlinks)
            {
                rendered = AnsiStyle.Hyperlink(url, AnsiStyle.Underline(label, caps.UseColor), caps.UseHyperlinks);
            }
            else
            {
                // No hyperlinks: print label and append URL (short form) for context.
                if (url.Length <= 60)
                {
                    rendered = label + " (" + url + ")";
                }
                else
                {
                    rendered = label + " (" + url.AsSpan(0, 57).ToString() + "...)";
                }
            }
            return Reserve(rendered);
        });

        // 7) Bold (** and __).
        var withBold = BoldRegex.Replace(withLinks, m =>
        {
            var inner = m.Groups[1].Value;
            return Reserve(AnsiStyle.Bold(inner, caps.UseColor));
        });
        withBold = BoldUnderscoreRegex.Replace(withBold, m =>
        {
            var inner = m.Groups[1].Value;
            return Reserve(AnsiStyle.Bold(inner, caps.UseColor));
        });

        // 8) Italic (* and _).
        var withItalic = ItalicRegex.Replace(withBold, m =>
        {
            var inner = m.Groups[1].Value;
            return Reserve(AnsiStyle.Italic(inner, caps.UseColor));
        });
        withItalic = ItalicUnderscoreRegex.Replace(withItalic, m =>
        {
            var inner = m.Groups[1].Value;
            return Reserve(AnsiStyle.Italic(inner, caps.UseColor));
        });

        // 9) Auto-link bare URLs.
        var withAuto = AutoLinkRegex.Replace(withItalic, m =>
        {
            var url = m.Value;
            var styled = caps.UseHyperlinks
                ? AnsiStyle.Hyperlink(url, AnsiStyle.Underline(url, caps.UseColor), caps.UseHyperlinks)
                : AnsiStyle.Underline(url, caps.UseColor);
            return Reserve(styled);
        });

        // 10) Issue keys.
        var withIssues = IssueKeyRegex.Replace(withAuto, m =>
        {
            var key = m.Value;
            var url = TrackerBaseUrl + "/" + key;
            var styled = caps.UseHyperlinks
                ? AnsiStyle.Hyperlink(url, AnsiStyle.Underline(key, caps.UseColor), caps.UseHyperlinks)
                : AnsiStyle.Underline(key, caps.UseColor);
            return Reserve(styled);
        });

        // Expand placeholders back.
        return ExpandPlaceholders(withIssues, placeholders);
    }

    /// <summary>
    /// Сканирует <paramref name="text"/> и заменяет escape-последовательности (<c>\X</c>,
    /// где <c>X</c> — спец-символ markdown'а) на placeholder'ы с литералом <c>X</c>.
    /// Backslash перед обычным символом сохраняется без изменений.
    /// </summary>
    private static string ApplyEscapes(string text, Func<string, string> reserve)
    {
        if (text.IndexOf('\\') < 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && EscapableChars.Contains(text[i + 1]))
            {
                // Reserve placeholder содержащий литерал — он будет восстановлен на финальном
                // шаге уже без какого-либо markdown-парсинга поверх.
                sb.Append(reserve(text[i + 1].ToString()));
                i += 2;
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Разрешает имя цвета (case-insensitive) в SGR payload (число/последовательность,
    /// которая ставится между <c>\e[</c> и <c>m</c>). Неизвестные имена возвращают
    /// <c>null</c>, и парсер выводит содержимое color-тега без markup.
    /// </summary>
    private static string? ResolveColorSgr(string name)
    {
        // Switch with a case-insensitive lookup. Используем lowered key
        // чтобы охватить варианты типа "Gray"/"GRAY".
        var key = name.ToLowerInvariant();
        return key switch
        {
            "gray" or "grey"                     => "90",        // bright black
            "red"                                => "31",
            "green"                              => "32",
            "yellow"                             => "33",
            "blue"                               => "34",
            "magenta" or "violet" or "purple"    => "35",
            "cyan"                               => "36",
            "white"                              => "37",
            "black"                              => "30",
            "orange"                             => "38;5;208",  // 256-color, brighter than yellow
            "pink"                               => "38;5;213",
            _                                    => null,
        };
    }

    /// <summary>
    /// Раскрывает плейсхолдеры в строке. Поддерживает вложенность: если рендер одного
    /// шага парсера сохраняет внутри плейсхолдеры от более раннего шага (например,
    /// color-тег обернул escaped-скобки), повторяем проход до тех пор, пока маркеры
    /// не исчезнут — но не более <c>maxPasses</c> раз чтобы исключить бесконечный цикл
    /// при патологических вводах.
    /// </summary>
    private static string ExpandPlaceholders(string s, List<string> placeholders)
    {
        if (placeholders.Count == 0)
        {
            return s;
        }

        const int maxPasses = 8;
        var current = s;
        for (var pass = 0; pass < maxPasses; pass++)
        {
            if (current.IndexOf(MarkerOpen) < 0)
            {
                break;
            }
            current = ExpandOnce(current, placeholders);
        }
        return current;
    }

    private static string ExpandOnce(string s, List<string> placeholders)
    {
        var sb = new StringBuilder(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == MarkerOpen)
            {
                // Find matching MarkerClose.
                var end = s.IndexOf(MarkerClose, i + 1);
                if (end < 0)
                {
                    sb.Append(c);
                    i++;
                    continue;
                }
                var idxText = s.Substring(i + 1, end - i - 1);
                if (int.TryParse(idxText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)
                    && idx >= 0 && idx < placeholders.Count)
                {
                    sb.Append(placeholders[idx]);
                    i = end + 1;
                    continue;
                }
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    private static string ResolveUrl(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }
        if (raw.StartsWith('/'))
        {
            return TrackerBaseUrl + raw;
        }
        return raw;
    }

    // -------- Word wrap --------

    /// <summary>
    /// Разбивает строку (возможно содержащую ANSI-коды) на список строк ширины не более
    /// <paramref name="width"/> (видимых символов). Не ломает ANSI-последовательности —
    /// они учитываются как 0-длинные.
    /// </summary>
    /// <param name="text">Строка с потенциальными ANSI-кодами.</param>
    /// <param name="width">Целевая видимая ширина.</param>
    /// <returns>Список строк (без trailing newline).</returns>
    public static List<string> WordWrap(string text, int width)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            result.Add(string.Empty);
            return result;
        }
        if (width <= 0)
        {
            result.Add(text);
            return result;
        }

        // Tokenize into words separated by spaces, preserving ANSI sequences inside words.
        var tokens = SplitWords(text);
        var line = new StringBuilder();
        var visibleLen = 0;

        foreach (var token in tokens)
        {
            var tokenVisible = AnsiStyle.VisibleLength(token);
            if (visibleLen == 0)
            {
                if (tokenVisible <= width)
                {
                    line.Append(token);
                    visibleLen = tokenVisible;
                }
                else
                {
                    // Token longer than width — emit as-is on its own line.
                    result.Add(token);
                }
                continue;
            }

            if (visibleLen + 1 + tokenVisible <= width)
            {
                line.Append(' ');
                line.Append(token);
                visibleLen += 1 + tokenVisible;
            }
            else
            {
                result.Add(line.ToString());
                line.Clear();
                if (tokenVisible <= width)
                {
                    line.Append(token);
                    visibleLen = tokenVisible;
                }
                else
                {
                    result.Add(token);
                    visibleLen = 0;
                }
            }
        }

        if (line.Length > 0)
        {
            result.Add(line.ToString());
        }

        if (result.Count == 0)
        {
            result.Add(string.Empty);
        }
        return result;
    }

    private static List<string> SplitWords(string text)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '' && i + 1 < text.Length)
            {
                var next = text[i + 1];
                if (next == '[')
                {
                    // CSI sequence — копируем целиком.
                    var start = i;
                    i += 2;
                    while (i < text.Length)
                    {
                        var ch = text[i];
                        i++;
                        if (ch >= 0x40 && ch <= 0x7E)
                        {
                            break;
                        }
                    }
                    sb.Append(text, start, i - start);
                    continue;
                }
                if (next == ']')
                {
                    // OSC sequence — копируем целиком.
                    var start = i;
                    i += 2;
                    while (i < text.Length)
                    {
                        var ch = text[i];
                        if (ch == '')
                        {
                            i++;
                            break;
                        }
                        if (ch == '' && i + 1 < text.Length && text[i + 1] == '\\')
                        {
                            i += 2;
                            break;
                        }
                        i++;
                    }
                    sb.Append(text, start, i - start);
                    continue;
                }
            }

            if (c == ' ' || c == '\t')
            {
                if (sb.Length > 0)
                {
                    words.Add(sb.ToString());
                    sb.Clear();
                }
                i++;
                continue;
            }

            sb.Append(c);
            i++;
        }

        if (sb.Length > 0)
        {
            words.Add(sb.ToString());
        }
        return words;
    }
}
