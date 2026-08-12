namespace YandexTrackerCLI.Core.Config;

using System.Text.Json.Serialization;

public sealed record Profile(
    [property: JsonPropertyName("org_type")]       OrgType OrgType,
    [property: JsonPropertyName("org_id")]         string OrgId,
    [property: JsonPropertyName("read_only")]      bool ReadOnly,
    [property: JsonPropertyName("auth")]           AuthConfig Auth,
    [property: JsonPropertyName("default_format")] string? DefaultFormat = null,
    [property: JsonPropertyName("allowed_queues")] string[]? AllowedQueues = null,
    [property: JsonPropertyName("allowed_write_issues")] string[]? AllowedWriteIssues = null);
