namespace YandexTrackerCLI.Core.Config;

/// <summary>
/// Профиль после резолва конфига, env-переменных и CLI-флагов.
/// </summary>
/// <param name="Name">Имя профиля.</param>
/// <param name="OrgType">Тип организации.</param>
/// <param name="OrgId">Идентификатор организации.</param>
/// <param name="ReadOnly">Действующая read-only политика.</param>
/// <param name="Auth">Действующая конфигурация аутентификации.</param>
/// <param name="DefaultFormat">Формат вывода по умолчанию из профиля, если задан.</param>
/// <param name="AllowedQueues">
/// Действующий список разрешённых очередей. <c>null</c> или пустой список означают
/// «ограничения нет» — профиль может обращаться к любой очереди.
/// </param>
/// <param name="AllowedWriteIssues">
/// Действующий список задач, открытых на запись. <c>null</c> или пустой список означают
/// «ограничения нет». Ограничение касается только записи: чтение любых задач
/// (в пределах <paramref name="AllowedQueues"/>) продолжает работать.
/// </param>
/// <param name="AllowedWriteIssuesFromEnv">
/// <c>true</c>, если действующий список задач пришёл из <c>YT_ALLOWED_WRITE_ISSUES</c>
/// и заменил значение профиля.
/// </param>
public sealed record EffectiveProfile(
    string Name,
    OrgType OrgType,
    string OrgId,
    bool ReadOnly,
    AuthConfig Auth,
    string? DefaultFormat = null,
    IReadOnlyList<string>? AllowedQueues = null,
    IReadOnlyList<string>? AllowedWriteIssues = null,
    bool AllowedWriteIssuesFromEnv = false);
