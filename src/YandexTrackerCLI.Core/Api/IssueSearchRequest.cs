namespace YandexTrackerCLI.Core.Api;

using System.Text.Json.Serialization;

/// <summary>
/// Тело запроса для <c>POST /v3/issues/_search</c>: YQL-выражение, по которому
/// Yandex Tracker возвращает постраничный список задач.
/// </summary>
/// <param name="Query">YQL-запрос, например <c>"Queue: DEV AND Status: open"</c>.</param>
public sealed record IssueSearchRequest(
    [property: JsonPropertyName("query")] string Query);
