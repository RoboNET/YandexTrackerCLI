namespace YandexTrackerCLI.Output;

using System.Globalization;
using System.Text;
using System.Text.Json;

/// <summary>
/// Рендерит одиночный issue-объект (<c>GET /v3/issues/{key}</c>) в форму detail view
/// для TTY: header (key + type + status + priority), summary, key-value метаданные
/// (queue / created / updated / assignee / tags), section-divider и markdown-описание.
/// </summary>
public static class IssueDetailRenderer
{
    /// <summary>
    /// Базовый URL Yandex Tracker для построения OSC 8 hyperlink на issue key.
    /// </summary>
    private const string TrackerBaseUrl = "https://tracker.yandex.ru";

    /// <summary>
    /// Метка-плейсхолдер для отсутствующих значений.
    /// </summary>
    private const string EmptyMarker = "—";

    /// <summary>
    /// Рендерит issue в <paramref name="writer"/>.
    /// </summary>
    /// <param name="writer">Куда выводить (обычно <see cref="PagerWriter.Create"/>).</param>
    /// <param name="issue">JSON-объект задачи от <c>GET /v3/issues/{key}</c>.</param>
    /// <param name="caps">Резолвленные возможности терминала.</param>
    public static void Render(TextWriter writer, JsonElement issue, TerminalCapabilities caps)
    {
        if (issue.ValueKind != JsonValueKind.Object)
        {
            // Не объект — fallback в table renderer.
            TableRenderer.Render(writer, issue, caps.Width);
            return;
        }

        var key = GetString(issue, "key");
        var type = ExtractDisplay(issue, "type");
        var status = ExtractDisplay(issue, "status");
        var priority = ExtractDisplay(issue, "priority");
        var summary = GetString(issue, "summary");

        WriteHeader(writer, key, type, status, priority, caps);
        writer.WriteLine();

        if (!string.IsNullOrEmpty(summary))
        {
            writer.WriteLine(AnsiStyle.Bold(summary, caps.UseColor));
            writer.WriteLine();
        }

        WriteMetadata(writer, issue, caps);

        var description = GetString(issue, "description");
        if (!string.IsNullOrEmpty(description))
        {
            writer.WriteLine();
            WriteSectionHeading(writer, "Description", caps);
            writer.WriteLine();
            writer.Write(MarkdownTerminalRenderer.Render(description, caps, leftIndent: 2));
        }
    }

    private static void WriteHeader(
        TextWriter writer,
        string? key,
        string? type,
        string? status,
        string? priority,
        TerminalCapabilities caps)
    {
        var parts = new List<string>(4);
        if (!string.IsNullOrEmpty(key))
        {
            var url = TrackerBaseUrl + "/" + key;
            var styled = AnsiStyle.Bold(key, caps.UseColor);
            parts.Add(AnsiStyle.Hyperlink(url, styled, caps.UseHyperlinks));
        }
        if (!string.IsNullOrEmpty(type)) parts.Add(type);
        if (!string.IsNullOrEmpty(status)) parts.Add(AnsiStyle.Bold(status, caps.UseColor));
        if (!string.IsNullOrEmpty(priority)) parts.Add(priority);

        writer.WriteLine(string.Join(" · ", parts));
    }

    private static void WriteMetadata(TextWriter writer, JsonElement issue, TerminalCapabilities caps)
    {
        var rows = new List<(string Key, string Value)>();

        var queue = ExtractQueueLine(issue);
        rows.Add(("Queue", queue ?? EmptyMarker));

        var createdAt = FormatTimestamp(GetString(issue, "createdAt"));
        var createdBy = ExtractDisplay(issue, "createdBy");
        var createdLine = createdAt is not null
            ? createdBy is not null ? createdAt + " by " + createdBy : createdAt
            : null;
        rows.Add(("Created", createdLine ?? EmptyMarker));

        var updatedAt = FormatTimestamp(GetString(issue, "updatedAt"));
        var updatedBy = ExtractDisplay(issue, "updatedBy");
        var updatedLine = updatedAt is not null
            ? updatedBy is not null ? updatedAt + " by " + updatedBy : updatedAt
            : null;
        rows.Add(("Updated", updatedLine ?? EmptyMarker));

        var assignee = ExtractDisplay(issue, "assignee");
        rows.Add(("Assignee", assignee ?? EmptyMarker));

        var tags = ExtractTags(issue);
        rows.Add(("Tags", tags ?? EmptyMarker));

        const int keyCol = 12;
        const string indent = "  ";
        foreach (var (k, v) in rows)
        {
            var padded = k.PadRight(keyCol);
            writer.Write(indent);
            writer.Write(AnsiStyle.Dim(padded, caps.UseColor));
            writer.WriteLine(v);
        }
    }

    /// <summary>
    /// Максимальная длина section divider в символах. На широких терминалах
    /// (200+ колонок) полная ширина выглядит дико; 80 — sweet spot для читаемости.
    /// </summary>
    private const int MaxSectionWidth = 80;

    private static void WriteSectionHeading(TextWriter writer, string title, TerminalCapabilities caps)
    {
        var prefix = "─ " + title + " ";
        var maxWidth = Math.Min(MaxSectionWidth, caps.Width);
        var trail = maxWidth - AnsiStyle.VisibleLength(prefix);
        if (trail < 4)
        {
            trail = 4;
        }
        var line = prefix + new string('─', trail);
        writer.WriteLine(AnsiStyle.Dim(line, caps.UseColor));
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
            JsonValueKind.True   => "true",
            JsonValueKind.False  => "false",
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

    private static string? ExtractQueueLine(JsonElement issue)
    {
        if (!issue.TryGetProperty("queue", out var q))
        {
            return null;
        }
        if (q.ValueKind == JsonValueKind.String)
        {
            return q.GetString();
        }
        if (q.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var display = q.TryGetProperty("display", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()
            : null;
        var key = q.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String
            ? k.GetString()
            : null;

        if (!string.IsNullOrEmpty(display) && !string.IsNullOrEmpty(key))
        {
            return display + " (" + key + ")";
        }
        if (!string.IsNullOrEmpty(display))
        {
            return display;
        }
        if (!string.IsNullOrEmpty(key))
        {
            return key;
        }
        return null;
    }

    private static string? ExtractTags(JsonElement issue)
    {
        if (!issue.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        if (tags.GetArrayLength() == 0)
        {
            return null;
        }
        var sb = new StringBuilder();
        var first = true;
        foreach (var tag in tags.EnumerateArray())
        {
            if (tag.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            var s = tag.GetString();
            if (string.IsNullOrEmpty(s))
            {
                continue;
            }
            if (!first)
            {
                sb.Append(", ");
            }
            first = false;
            sb.Append(s);
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>
    /// Форматирует ISO-8601 timestamp в форму <c>YYYY-MM-DD HH:MM UTC</c>. Если строка
    /// не парсится — возвращается as-is.
    /// </summary>
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
