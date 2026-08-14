namespace YandexTrackerCLI.Core.Config;

using System.Text.Json.Serialization;

/// <summary>
/// Профиль в файле конфигурации: организация, креденшелы и политики.
/// </summary>
/// <param name="OrgType">Тип организации.</param>
/// <param name="OrgId">Идентификатор организации.</param>
/// <param name="ReadOnly">Профиль только на чтение.</param>
/// <param name="Auth">Конфигурация аутентификации.</param>
/// <param name="DefaultFormat">Формат вывода по умолчанию.</param>
/// <param name="AllowedQueues">Список очередей, к которым профиль вправе обращаться.</param>
/// <param name="AllowedWriteIssues">Список задач, открытых профилю на запись.</param>
/// <param name="ExternalEffects">
/// Политика «не инициировать внешние эффекты»: <c>false</c> запрещает призыв
/// (<c>summonees</c>/<c>maillistSummonees</c>) и мутации автоматизаций
/// (<c>triggers</c>/<c>autoactions</c>). <c>null</c> (ключа нет) и <c>true</c> означают
/// «разрешено» — поэтому отсутствие ключа не меняет поведение существующих профилей.
/// </param>
public sealed record Profile(
    [property: JsonPropertyName("org_type")]       OrgType OrgType,
    [property: JsonPropertyName("org_id")]         string OrgId,
    [property: JsonPropertyName("read_only")]      bool ReadOnly,
    [property: JsonPropertyName("auth")]           AuthConfig Auth,
    [property: JsonPropertyName("default_format")] string? DefaultFormat = null,
    [property: JsonPropertyName("allowed_queues")] string[]? AllowedQueues = null,
    [property: JsonPropertyName("allowed_write_issues")] string[]? AllowedWriteIssues = null,
    [property: JsonPropertyName("external_effects")] bool? ExternalEffects = null);
