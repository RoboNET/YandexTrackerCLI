namespace YandexTrackerCLI.Commands.Issue;

using System.Globalization;
using System.Text;
using Core.Api.Errors;

/// <summary>
/// Набор значений CLI-фильтров для <c>yt issue find</c>. Каждое поле соответствует
/// одноимённой опции команды: либо произвольный <c>--yql</c>, либо набор «простых»
/// фильтров, транслируемых в YQL билдером <see cref="IssueFilterBuilder"/>.
/// </summary>
/// <param name="Yql">Сырой YQL-запрос. Взаимоисключается с simple-фильтрами.</param>
/// <param name="Queue">Ключ очереди (например <c>DEV</c>).</param>
/// <param name="Status">Один статус или список через запятую (<c>open,in-progress</c>).</param>
/// <param name="Assignee">Логин, либо специальное значение <c>me</c>.</param>
/// <param name="Type">Один тип или список через запятую (<c>bug,task</c>).</param>
/// <param name="Priority">Приоритет (например <c>minor</c>).</param>
/// <param name="UpdatedSince">Дата нижней границы обновления (ISO-8601).</param>
/// <param name="CreatedSince">Дата нижней границы создания (ISO-8601).</param>
/// <param name="Text">Полнотекстовый фрагмент; ищется в <c>Summary</c> или <c>Description</c>.</param>
/// <param name="Tag">Один тег или список через запятую.</param>
public sealed record IssueFilters(
    string? Yql,
    string? Queue,
    string? Status,
    string? Assignee,
    string? Type,
    string? Priority,
    string? UpdatedSince,
    string? CreatedSince,
    string? Text,
    string? Tag);

/// <summary>
/// Чистый транслятор CLI-фильтров в YQL-строку. Не зависит от HTTP: детерминированная
/// функция, удобна для unit-тестирования. Используется командой <c>yt issue find</c>.
/// </summary>
public static class IssueFilterBuilder
{
    /// <summary>
    /// Собирает YQL-выражение из набора <see cref="IssueFilters"/>.
    /// Либо возвращает исходный <c>--yql</c> как есть (после проверки на управляющие символы),
    /// либо конкатенирует простые фильтры через <c>AND</c>.
    /// </summary>
    /// <param name="f">Значения опций команды.</param>
    /// <returns>Строковое YQL-выражение.</returns>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.InvalidArgs"/>, если одновременно заданы <c>--yql</c> и simple-фильтры,
    /// нет ни одного фильтра, значения содержат управляющие символы/CRLF,
    /// либо неверно распарсилась дата.
    /// </exception>
    public static string Build(IssueFilters f)
    {
        var hasYql = !string.IsNullOrWhiteSpace(f.Yql);
        var hasSimple = AnySimple(f);

        if (hasYql && hasSimple)
        {
            throw new TrackerException(
                ErrorCode.InvalidArgs,
                "Use either --yql or simple filters, not both.");
        }

        if (!hasYql && !hasSimple)
        {
            throw new TrackerException(
                ErrorCode.InvalidArgs,
                "At least one filter is required (use --yql or --queue/--status/...).");
        }

        if (hasYql)
        {
            CheckSafe(f.Yql!, "--yql");
            return f.Yql!;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(f.Queue))
        {
            parts.Add($"Queue: {QuoteValue(f.Queue, "--queue")}");
        }

        if (!string.IsNullOrWhiteSpace(f.Status))
        {
            parts.Add($"Status: {ListOrSingle(f.Status, "--status")}");
        }

        if (!string.IsNullOrWhiteSpace(f.Assignee))
        {
            parts.Add(BuildAssignee(f.Assignee));
        }

        if (!string.IsNullOrWhiteSpace(f.Type))
        {
            parts.Add($"Type: {ListOrSingle(f.Type, "--type")}");
        }

        if (!string.IsNullOrWhiteSpace(f.Priority))
        {
            parts.Add($"Priority: {QuoteValue(f.Priority, "--priority")}");
        }

        if (!string.IsNullOrWhiteSpace(f.UpdatedSince))
        {
            parts.Add($"Updated: >={QuoteDate(f.UpdatedSince, "--updated-since")}");
        }

        if (!string.IsNullOrWhiteSpace(f.CreatedSince))
        {
            parts.Add($"Created: >={QuoteDate(f.CreatedSince, "--created-since")}");
        }

        if (!string.IsNullOrWhiteSpace(f.Text))
        {
            var q = QuoteValue(f.Text, "--text");
            parts.Add($"(Summary: {q} OR Description: {q})");
        }

        if (!string.IsNullOrWhiteSpace(f.Tag))
        {
            parts.Add($"Tags: {ListOrSingle(f.Tag, "--tag")}");
        }

        return string.Join(" AND ", parts);
    }

    /// <summary>
    /// Возвращает <c>true</c>, если задан хотя бы один simple-фильтр.
    /// </summary>
    private static bool AnySimple(IssueFilters f) =>
        !string.IsNullOrWhiteSpace(f.Queue) ||
        !string.IsNullOrWhiteSpace(f.Status) ||
        !string.IsNullOrWhiteSpace(f.Assignee) ||
        !string.IsNullOrWhiteSpace(f.Type) ||
        !string.IsNullOrWhiteSpace(f.Priority) ||
        !string.IsNullOrWhiteSpace(f.UpdatedSince) ||
        !string.IsNullOrWhiteSpace(f.CreatedSince) ||
        !string.IsNullOrWhiteSpace(f.Text) ||
        !string.IsNullOrWhiteSpace(f.Tag);

    /// <summary>
    /// Строит YQL-фрагмент для <c>--assignee</c>: специальное значение <c>me</c>
    /// транслируется в функцию <c>me()</c>, остальное — как строковый литерал.
    /// </summary>
    private static string BuildAssignee(string raw)
    {
        CheckSafe(raw, "--assignee");
        if (raw == "me")
        {
            return "Assignee: me()";
        }

        return $"Assignee: {QuoteValue(raw, "--assignee")}";
    }

    /// <summary>
    /// Преобразует значение CSV-фильтра либо в единичный литерал <c>"value"</c>,
    /// либо в YQL-список <c>("a", "b", "c")</c>.
    /// </summary>
    private static string ListOrSingle(string raw, string source)
    {
        var parts = raw.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            throw new TrackerException(ErrorCode.InvalidArgs, $"{source} is empty.");
        }

        if (parts.Length == 1)
        {
            return QuoteValue(parts[0], source);
        }

        var sb = new StringBuilder();
        sb.Append('(');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(QuoteValue(parts[i], source));
        }

        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// Оборачивает значение в двойные кавычки с экранированием обратной косой черты и кавычек.
    /// </summary>
    private static string QuoteValue(string value, string source)
    {
        CheckSafe(value, source);
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return "\"" + escaped + "\"";
    }

    /// <summary>
    /// Проверяет корректность даты/времени через <see cref="DateTimeOffset.TryParse(string, IFormatProvider, DateTimeStyles, out DateTimeOffset)"/>
    /// и возвращает исходную строку в кавычках.
    /// </summary>
    private static string QuoteDate(string value, string source)
    {
        CheckSafe(value, source);
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out _))
        {
            throw new TrackerException(
                ErrorCode.InvalidArgs,
                $"{source}: invalid date/time '{value}'.");
        }

        return "\"" + value + "\"";
    }

    /// <summary>
    /// Отвергает значения с управляющими символами (включая CR/LF), чтобы такие подстроки
    /// не просачивались в итоговый YQL и не ломали запрос.
    /// </summary>
    private static void CheckSafe(string value, string source)
    {
        foreach (var c in value)
        {
            if (c == '\r' || c == '\n' || char.IsControl(c))
            {
                throw new TrackerException(
                    ErrorCode.InvalidArgs,
                    $"{source} contains control/CRLF characters.");
            }
        }
    }
}
