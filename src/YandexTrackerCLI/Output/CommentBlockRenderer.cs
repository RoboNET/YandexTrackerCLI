namespace YandexTrackerCLI.Output;

using System.Globalization;
using System.Text.Json;

/// <summary>
/// Рендерит массив комментариев Tracker'а в форму blocks-view: для каждого комментария
/// заголовок (автор · дата [· edited]) и текст с markdown-форматированием.
/// </summary>
public static class CommentBlockRenderer
{
    /// <summary>
    /// Префикс заголовка блока (горизонтальная черта).
    /// </summary>
    private const string HeadingPrefix = "─ ";

    /// <summary>
    /// Максимальная длина divider'а в заголовке комментария. Cap'ится здесь чтобы
    /// на широких терминалах (200+ колонок) черта не растягивалась на весь экран.
    /// </summary>
    private const int MaxHeaderWidth = 80;

    /// <summary>
    /// Рендерит массив комментариев в <paramref name="writer"/>.
    /// </summary>
    /// <param name="writer">Куда выводить.</param>
    /// <param name="commentsArray">JSON-массив комментариев.</param>
    /// <param name="caps">Резолвленные возможности терминала.</param>
    public static void Render(TextWriter writer, JsonElement commentsArray, TerminalCapabilities caps)
    {
        if (commentsArray.ValueKind != JsonValueKind.Array)
        {
            // Не массив — пробрасываем в TableRenderer как fallback.
            TableRenderer.Render(writer, commentsArray, caps.Width);
            return;
        }

        var first = true;
        foreach (var comment in commentsArray.EnumerateArray())
        {
            if (comment.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!first)
            {
                writer.WriteLine();
            }
            first = false;

            RenderComment(writer, comment, caps);
        }

        if (first)
        {
            writer.WriteLine("(no comments)");
        }
    }

    private static void RenderComment(TextWriter writer, JsonElement comment, TerminalCapabilities caps)
    {
        var author = ExtractDisplay(comment, "createdBy") ?? ExtractDisplay(comment, "updatedBy") ?? "unknown";
        var createdAt = FormatTimestamp(GetString(comment, "createdAt"));
        var updatedAt = GetString(comment, "updatedAt");

        var headerParts = new List<string>(3)
        {
            AnsiStyle.Bold(author, caps.UseColor),
        };
        if (!string.IsNullOrEmpty(createdAt))
        {
            headerParts.Add(createdAt);
        }
        if (!string.IsNullOrEmpty(updatedAt)
            && !string.Equals(updatedAt, GetString(comment, "createdAt"), StringComparison.Ordinal))
        {
            headerParts.Add(AnsiStyle.Dim("(edited)", caps.UseColor));
        }

        var headerText = HeadingPrefix + string.Join(" · ", headerParts) + " ";
        var maxWidth = Math.Min(MaxHeaderWidth, caps.Width);
        var trail = maxWidth - AnsiStyle.VisibleLength(headerText);
        if (trail < 4)
        {
            trail = 4;
        }
        var headerLine = headerText + new string('─', trail);
        writer.WriteLine(AnsiStyle.Dim(new string('─', 0), caps.UseColor) + headerLine);

        var text = GetString(comment, "text");
        if (string.IsNullOrEmpty(text))
        {
            writer.WriteLine();
            writer.WriteLine(AnsiStyle.Dim("  (empty)", caps.UseColor));
            return;
        }

        writer.WriteLine();
        writer.Write(MarkdownTerminalRenderer.Render(text, caps, leftIndent: 2));
    }

    private static string? GetString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var prop))
        {
            return null;
        }
        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            _                    => null,
        };
    }

    private static string? ExtractDisplay(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var prop))
        {
            return null;
        }
        if (prop.ValueKind == JsonValueKind.Object)
        {
            foreach (var field in new[] { "display", "name", "key", "id", "login" })
            {
                if (prop.TryGetProperty(field, out var sub) && sub.ValueKind == JsonValueKind.String)
                {
                    var s = sub.GetString();
                    if (!string.IsNullOrEmpty(s))
                    {
                        return s;
                    }
                }
            }
            return null;
        }
        if (prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static string? FormatTimestamp(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }
        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
        {
            return dto.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
        }
        return raw;
    }
}
