namespace YandexTrackerCLI.Output;

using System.Globalization;
using System.Text;
using System.Text.Json;

/// <summary>
/// Рендерер человекочитаемой таблицы для TTY: 2-column key-value для одиночной сущности,
/// многоколоночная — для массивов объектов.
/// </summary>
/// <remarks>
/// AOT-safe: работает только с <see cref="JsonElement"/> и <see cref="JsonValueKind"/>.
/// Использует юникод-символ <c>U+2500</c> (─) для разделителей. ANSI цвета не применяются —
/// рендерер plain-text.
/// </remarks>
public static class TableRenderer
{
    /// <summary>
    /// Ширина по умолчанию, когда <see cref="Console.WindowWidth"/> недоступен
    /// (тесты, redirected output, отсутствие TTY).
    /// </summary>
    private const int DefaultWidth = 100;

    /// <summary>
    /// Максимум объектов в array-tabular рендере.
    /// </summary>
    private const int MaxArrayRows = 50;

    /// <summary>
    /// Максимум колонок в array-tabular рендере.
    /// </summary>
    private const int MaxArrayColumns = 8;

    /// <summary>
    /// Identifying-ключи: первая колонка для array-tabular, в порядке предпочтения.
    /// </summary>
    private static readonly string[] IdentifyingKeys = { "key", "id" };

    /// <summary>
    /// "Богатые" ключи, которые желательно показывать после identifying.
    /// </summary>
    private static readonly string[] PreferredKeys = { "display", "name", "summary", "title", "login" };

    /// <summary>
    /// Identifying-поля для извлечения краткого значения из вложенного объекта
    /// (smart flatten в key-value таблице).
    /// </summary>
    private static readonly string[] NestedIdentifyingFields =
    {
        "display", "key", "name", "id", "login", "summary",
    };

    /// <summary>
    /// Рендерит элемент в table-формат.
    /// </summary>
    /// <param name="writer">Куда писать.</param>
    /// <param name="element">JSON элемент: object, array или primitive.</param>
    /// <param name="terminalWidth">Опциональная ширина терминала; если не задана, используется
    /// <see cref="Console.WindowWidth"/> или <see cref="DefaultWidth"/>.</param>
    public static void Render(TextWriter writer, JsonElement element, int? terminalWidth = null)
    {
        var width = ResolveWidth(terminalWidth);

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                RenderObjectTable(writer, element, width);
                break;

            case JsonValueKind.Array:
                RenderArray(writer, element, width);
                break;

            default:
                writer.WriteLine(RenderPrimitive(element));
                break;
        }
    }

    private static int ResolveWidth(int? requested)
    {
        if (requested is { } w and > 0)
        {
            return w;
        }

        try
        {
            var consoleWidth = Console.WindowWidth;
            if (consoleWidth > 0)
            {
                return consoleWidth;
            }
        }
        catch
        {
            // Console.WindowWidth кидает в non-TTY (redirected output) — fallback.
        }

        return DefaultWidth;
    }

    /// <summary>
    /// Рендерит одиночный объект как 2-column key-value таблицу.
    /// </summary>
    private static void RenderObjectTable(TextWriter writer, JsonElement obj, int width)
    {
        var rows = new List<(string Key, string Value)>();
        foreach (var prop in obj.EnumerateObject())
        {
            rows.Add((prop.Name, RenderCellValue(prop.Value)));
        }

        if (rows.Count == 0)
        {
            writer.WriteLine("(empty object)");
            return;
        }

        const string keyHeader = "key";
        const string valueHeader = "value";

        var keyCol = Math.Max(keyHeader.Length, rows.Max(r => r.Key.Length));
        // Отступ между колонками — 2 пробела.
        var valueCol = Math.Max(valueHeader.Length, width - keyCol - 2);
        if (valueCol < 8)
        {
            valueCol = 8;
        }

        WriteRow(writer, keyHeader, keyCol, valueHeader, valueCol);
        WriteRow(writer, new string('─', keyCol), keyCol, new string('─', valueCol), valueCol);
        foreach (var (k, v) in rows)
        {
            WriteRow(writer, k, keyCol, Truncate(v, valueCol), valueCol);
        }
    }

    /// <summary>
    /// Рендерит массив. Объекты идут как многоколоночная таблица; примитивы —
    /// bullet-list (<c>- value</c>).
    /// </summary>
    private static void RenderArray(TextWriter writer, JsonElement arr, int width)
    {
        var len = arr.GetArrayLength();
        if (len == 0)
        {
            writer.WriteLine("(empty array)");
            return;
        }

        // Если все элементы — примитивы, используем bullet list.
        var allPrimitive = true;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
            {
                allPrimitive = false;
                break;
            }
        }

        if (allPrimitive)
        {
            foreach (var item in arr.EnumerateArray())
            {
                writer.Write("- ");
                writer.WriteLine(RenderPrimitive(item));
            }
            return;
        }

        // Array of objects — table.
        var objects = new List<JsonElement>(Math.Min(len, MaxArrayRows));
        var truncated = false;
        var seen = 0;
        foreach (var item in arr.EnumerateArray())
        {
            seen++;
            if (item.ValueKind != JsonValueKind.Object)
            {
                // Не объект в массиве объектов — fallback в compact JSON-строку.
                continue;
            }
            if (objects.Count < MaxArrayRows)
            {
                objects.Add(item);
            }
        }
        if (seen > MaxArrayRows)
        {
            truncated = true;
        }

        if (objects.Count == 0)
        {
            // Все элементы были не-object — печатаем как bullet list compact-JSON.
            foreach (var item in arr.EnumerateArray())
            {
                writer.Write("- ");
                writer.WriteLine(RenderPrimitive(item));
            }
            return;
        }

        var columns = SelectColumns(objects);
        var widths = ComputeColumnWidths(columns, objects, width);

        // Header.
        WriteCells(writer, columns, widths);
        // Separator.
        var sep = new string[columns.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            sep[i] = new string('─', widths[i]);
        }
        WriteCells(writer, sep, widths);

        // Rows.
        foreach (var row in objects)
        {
            var cells = new string[columns.Length];
            for (var i = 0; i < columns.Length; i++)
            {
                if (row.TryGetProperty(columns[i], out var prop))
                {
                    cells[i] = Truncate(RenderCellValue(prop), widths[i]);
                }
                else
                {
                    cells[i] = string.Empty;
                }
            }
            WriteCells(writer, cells, widths);
        }

        if (truncated)
        {
            writer.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "({0} of {1} rows shown)",
                MaxArrayRows,
                seen));
        }
    }

    /// <summary>
    /// Подбирает колонки для array-tabular рендера: identifying первой,
    /// preferred затем по частоте, ограничено <see cref="MaxArrayColumns"/>.
    /// </summary>
    private static string[] SelectColumns(List<JsonElement> objects)
    {
        // Считаем частоту встречаемости каждого ключа.
        var freq = new Dictionary<string, int>(StringComparer.Ordinal);
        var firstSeenOrder = new List<string>();
        foreach (var obj in objects)
        {
            foreach (var p in obj.EnumerateObject())
            {
                if (!freq.ContainsKey(p.Name))
                {
                    freq[p.Name] = 0;
                    firstSeenOrder.Add(p.Name);
                }
                freq[p.Name]++;
            }
        }

        var ordered = new List<string>(MaxArrayColumns);
        var added = new HashSet<string>(StringComparer.Ordinal);

        // 1. Identifying первой.
        foreach (var name in IdentifyingKeys)
        {
            if (freq.ContainsKey(name) && added.Add(name))
            {
                ordered.Add(name);
                if (ordered.Count >= MaxArrayColumns) return ordered.ToArray();
                break; // только одно identifying в начало
            }
        }

        // 2. Preferred — в порядке списка.
        foreach (var name in PreferredKeys)
        {
            if (freq.ContainsKey(name) && added.Add(name))
            {
                ordered.Add(name);
                if (ordered.Count >= MaxArrayColumns) return ordered.ToArray();
            }
        }

        // 3. Остальное — по частоте, при равенстве — по порядку первого появления.
        var remaining = new List<string>();
        foreach (var name in firstSeenOrder)
        {
            if (!added.Contains(name))
            {
                remaining.Add(name);
            }
        }
        remaining.Sort((a, b) =>
        {
            var byFreq = freq[b].CompareTo(freq[a]);
            if (byFreq != 0) return byFreq;
            return firstSeenOrder.IndexOf(a).CompareTo(firstSeenOrder.IndexOf(b));
        });

        foreach (var name in remaining)
        {
            ordered.Add(name);
            if (ordered.Count >= MaxArrayColumns) break;
        }

        return ordered.ToArray();
    }

    /// <summary>
    /// Рассчитывает ширины колонок: подгоняет под содержимое, общая ширина ≤ <paramref name="totalWidth"/>.
    /// </summary>
    private static int[] ComputeColumnWidths(string[] columns, List<JsonElement> rows, int totalWidth)
    {
        var widths = new int[columns.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            widths[i] = columns[i].Length;
            foreach (var row in rows)
            {
                if (row.TryGetProperty(columns[i], out var prop))
                {
                    var v = RenderCellValue(prop);
                    if (v.Length > widths[i])
                    {
                        widths[i] = v.Length;
                    }
                }
            }
        }

        // Limit total width: учтём gap=2 между колонками.
        var gap = 2;
        var available = totalWidth - gap * (columns.Length - 1);
        if (available <= columns.Length * 4)
        {
            // Слишком узко: даём минимум 4 на колонку.
            for (var i = 0; i < widths.Length; i++)
            {
                widths[i] = 4;
            }
            return widths;
        }

        var sum = widths.Sum();
        if (sum <= available)
        {
            return widths;
        }

        // Пропорционально ужимаем самые широкие колонки до тех пор, пока не уложимся.
        // Делаем простой алгоритм: пока sum > available, вычитаем 1 у самой широкой.
        while (sum > available)
        {
            var maxIdx = 0;
            for (var i = 1; i < widths.Length; i++)
            {
                if (widths[i] > widths[maxIdx]) maxIdx = i;
            }
            if (widths[maxIdx] <= 4) break; // не дальше
            widths[maxIdx]--;
            sum--;
        }

        return widths;
    }

    /// <summary>
    /// Записывает строку из ячеек, разделённых двумя пробелами.
    /// </summary>
    private static void WriteCells(TextWriter writer, string[] cells, int[] widths)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0)
            {
                writer.Write("  ");
            }
            // Last cell: не паддим хвостом, чтобы не было лишних пробелов в конце.
            if (i == cells.Length - 1)
            {
                writer.Write(Truncate(cells[i], widths[i]));
            }
            else
            {
                writer.Write(PadRightSafe(Truncate(cells[i], widths[i]), widths[i]));
            }
        }
        writer.WriteLine();
    }

    private static void WriteRow(TextWriter writer, string col1, int width1, string col2, int width2)
    {
        writer.Write(PadRightSafe(col1, width1));
        writer.Write("  ");
        writer.Write(Truncate(col2, width2));
        writer.WriteLine();
    }

    private static string PadRightSafe(string s, int width) =>
        s.Length >= width ? s : s + new string(' ', width - s.Length);

    /// <summary>
    /// Обрезает строку до <paramref name="max"/> символов, добавляя <c>…</c> при обрезке.
    /// </summary>
    private static string Truncate(string s, int max)
    {
        if (max <= 0) return string.Empty;
        if (s.Length <= max) return s;
        if (max == 1) return "…";
        return s.AsSpan(0, max - 1).ToString() + "…";
    }

    /// <summary>
    /// Возвращает строковое значение для ячейки таблицы.
    /// Применяет smart-flatten: для nested object вытаскивает identifying-поле,
    /// для array — формирует <c>[a, b, c]</c> если все примитивы и помещается в 60 chars,
    /// иначе <c>[N items]</c>.
    /// </summary>
    private static string RenderCellValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String  => EscapeOneLine(el.GetString() ?? string.Empty),
        JsonValueKind.Number  => el.GetRawText(),
        JsonValueKind.True    => "true",
        JsonValueKind.False   => "false",
        JsonValueKind.Null    => string.Empty,
        JsonValueKind.Object  => RenderNestedObject(el),
        JsonValueKind.Array   => RenderNestedArray(el),
        _                     => el.GetRawText(),
    };

    private static string RenderNestedObject(JsonElement obj)
    {
        foreach (var name in NestedIdentifyingFields)
        {
            if (obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var s = prop.GetString();
                if (!string.IsNullOrEmpty(s))
                {
                    return EscapeOneLine(s);
                }
            }
        }
        return "{...}";
    }

    private static string RenderNestedArray(JsonElement arr)
    {
        var len = arr.GetArrayLength();
        if (len == 0) return "[]";

        var allPrimitive = true;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
            {
                allPrimitive = false;
                break;
            }
        }

        if (allPrimitive)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            var first = true;
            foreach (var item in arr.EnumerateArray())
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(EscapeOneLine(RenderPrimitiveCellInline(item)));
                if (sb.Length > 60)
                {
                    return string.Format(CultureInfo.InvariantCulture, "[{0} items]", len);
                }
            }
            sb.Append(']');
            return sb.ToString();
        }

        return string.Format(CultureInfo.InvariantCulture, "[{0} items]", len);
    }

    /// <summary>
    /// Inline-рендер примитива для вложенного массива (без обрамляющих кавычек строки).
    /// </summary>
    private static string RenderPrimitiveCellInline(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString() ?? string.Empty,
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True   => "true",
        JsonValueKind.False  => "false",
        JsonValueKind.Null   => "null",
        _                    => el.GetRawText(),
    };

    private static string RenderPrimitive(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString() ?? string.Empty,
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True   => "true",
        JsonValueKind.False  => "false",
        JsonValueKind.Null   => string.Empty,
        _                    => el.GetRawText(),
    };

    /// <summary>
    /// Заменяет переводы строк/табы на пробелы, чтобы значение помещалось в одну строку таблицы.
    /// </summary>
    private static string EscapeOneLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.IndexOfAny(new[] { '\n', '\r', '\t' }) < 0) return s;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == '\n' || ch == '\r' || ch == '\t')
            {
                sb.Append(' ');
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }
}
