namespace YandexTrackerCLI.Output;

/// <summary>
/// Хелперы для оборачивания текста в ANSI-escape sequences и OSC 8 hyperlinks.
/// Все методы принимают boolean-флаг и при <c>false</c> возвращают исходный текст
/// без изменений (используется при не-TTY выводе).
/// </summary>
public static class AnsiStyle
{
    /// <summary>
    /// Префикс CSI: ESC [.
    /// </summary>
    private const string Csi = "[";

    /// <summary>
    /// Префикс OSC: ESC ].
    /// </summary>
    private const string Osc = "]";

    /// <summary>
    /// String Terminator: ESC \.
    /// </summary>
    private const string St = "\\";

    /// <summary>
    /// Сброс всех атрибутов: <c>\e[0m</c>.
    /// </summary>
    public const string Reset = "[0m";

    /// <summary>
    /// Оборачивает текст в bold (<c>\e[1m</c>...<c>\e[22m</c>).
    /// </summary>
    /// <param name="text">Исходный текст.</param>
    /// <param name="useColor">Если <c>false</c>, возвращает <paramref name="text"/> как есть.</param>
    public static string Bold(string text, bool useColor) =>
        useColor ? Csi + "1m" + text + Csi + "22m" : text;

    /// <summary>
    /// Оборачивает текст в italic (<c>\e[3m</c>...<c>\e[23m</c>).
    /// </summary>
    /// <param name="text">Исходный текст.</param>
    /// <param name="useColor">Если <c>false</c>, возвращает <paramref name="text"/> как есть.</param>
    public static string Italic(string text, bool useColor) =>
        useColor ? Csi + "3m" + text + Csi + "23m" : text;

    /// <summary>
    /// Оборачивает текст в dim/faint (<c>\e[2m</c>...<c>\e[22m</c>).
    /// </summary>
    /// <param name="text">Исходный текст.</param>
    /// <param name="useColor">Если <c>false</c>, возвращает <paramref name="text"/> как есть.</param>
    public static string Dim(string text, bool useColor) =>
        useColor ? Csi + "2m" + text + Csi + "22m" : text;

    /// <summary>
    /// Оборачивает текст в underline (<c>\e[4m</c>...<c>\e[24m</c>).
    /// </summary>
    /// <param name="text">Исходный текст.</param>
    /// <param name="useColor">Если <c>false</c>, возвращает <paramref name="text"/> как есть.</param>
    public static string Underline(string text, bool useColor) =>
        useColor ? Csi + "4m" + text + Csi + "24m" : text;

    /// <summary>
    /// Подсветка inline-кода: reverse video (<c>\e[7m</c>...<c>\e[27m</c>) — выглядит
    /// как «инвертированный» прямоугольник, что неплохо имитирует <c>backtick</c>-фон
    /// в большинстве терминалов и не зависит от цветовой схемы.
    /// </summary>
    /// <param name="text">Исходный текст.</param>
    /// <param name="useColor">Если <c>false</c>, возвращает <paramref name="text"/> как есть.</param>
    public static string CodeInline(string text, bool useColor) =>
        useColor ? Csi + "7m" + text + Csi + "27m" : text;

    /// <summary>
    /// Оборачивает текст в strikethrough (<c>\e[9m</c>...<c>\e[29m</c>) — GFM-расширение
    /// markdown'а (<c>~~text~~</c>).
    /// </summary>
    /// <param name="text">Исходный текст.</param>
    /// <param name="useColor">Если <c>false</c>, возвращает <paramref name="text"/> как есть.</param>
    public static string Strikethrough(string text, bool useColor) =>
        useColor ? Csi + "9m" + text + Csi + "29m" : text;

    /// <summary>
    /// Оборачивает текст в SGR foreground colour. <paramref name="sgr"/> — payload между
    /// <c>\e[</c> и <c>m</c> (например, <c>"31"</c> для red, <c>"38;5;208"</c> для orange);
    /// reset выполняется через <c>\e[39m</c> (default foreground), что не сбрасывает другие
    /// атрибуты (bold/italic/strikethrough могут быть наложены сверху).
    /// </summary>
    /// <param name="sgr">SGR payload без префикса CSI и без trailing 'm'.</param>
    /// <param name="text">Исходный текст.</param>
    /// <param name="useColor">Если <c>false</c>, возвращает <paramref name="text"/> как есть (без markup).</param>
    public static string ForegroundSgr(string sgr, string text, bool useColor) =>
        useColor ? Csi + sgr + "m" + text + Csi + "39m" : text;

    /// <summary>
    /// Оборачивает текст в OSC 8 hyperlink: <c>\e]8;;URL\e\\TEXT\e]8;;\e\\</c>.
    /// </summary>
    /// <param name="url">URL ссылки.</param>
    /// <param name="text">Видимый текст.</param>
    /// <param name="useHyperlinks">Если <c>false</c>, возвращает <paramref name="text"/> как есть.</param>
    public static string Hyperlink(string url, string text, bool useHyperlinks)
    {
        if (!useHyperlinks)
        {
            return text;
        }
        // ESC ] 8 ; ; URL ST ... ESC ] 8 ; ; ST
        // ST = ESC \  (preferred form, supported by all major terminals).
        return Osc + "8;;" + url + St + text + Osc + "8;;" + St;
    }

    /// <summary>
    /// Возвращает количество видимых символов в строке, игнорируя ANSI escape-последовательности
    /// (<c>CSI</c> <c>\e[...m</c>) и OSC 8 hyperlinks (<c>\e]...\e\\</c>).
    /// Используется для расчёта word-wrap и табличного выравнивания.
    /// </summary>
    /// <param name="text">Строка, возможно содержащая ANSI-коды.</param>
    /// <returns>Число видимых code points.</returns>
    public static int VisibleLength(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        const char esc = '';
        const char bel = '';

        var len = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == esc && i + 1 < text.Length)
            {
                var next = text[i + 1];
                if (next == '[')
                {
                    // CSI: ESC [ ... <final byte 0x40-0x7E>
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
                    continue;
                }
                if (next == ']')
                {
                    // OSC: ESC ] ... ST. ST is ESC \ (preferred) or BEL (0x07).
                    i += 2;
                    while (i < text.Length)
                    {
                        var ch = text[i];
                        if (ch == bel)
                        {
                            i++;
                            break;
                        }
                        if (ch == esc && i + 1 < text.Length && text[i + 1] == '\\')
                        {
                            i += 2;
                            break;
                        }
                        i++;
                    }
                    continue;
                }
            }

            // Skip surrogate pair as a single code point.
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                len++;
                i += 2;
                continue;
            }

            len++;
            i++;
        }

        return len;
    }
}
