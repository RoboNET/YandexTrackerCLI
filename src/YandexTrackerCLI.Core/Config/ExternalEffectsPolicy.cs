namespace YandexTrackerCLI.Core.Config;

using System.Text.Json;
using Api.Errors;

/// <summary>
/// Политика <c>external_effects</c>: «не инициировать рассылку и интеграции явно».
/// Когда она выключена (<c>external_effects: false</c> в профиле либо
/// <c>YT_EXTERNAL_EFFECTS=0</c>), запрещены два класса мутирующих обращений — призыв
/// (<c>summonees</c>, <c>maillistSummonees</c> в теле) и мутации автоматизаций
/// (<c>triggers</c>, <c>autoactions</c> в пути). Чтение не ограничивается.
/// </summary>
/// <remarks>
/// <para>
/// <b>Это не гарантия отсутствия внешних эффектов.</b> Любая правка задачи может поднять
/// триггер, уже настроенный на стороне очереди, и он отправит письмо или HTTP-запрос
/// наружу — CLI об этом не знает и повлиять на это не может. Политика ограничивает ровно
/// то, что инициирует сам вызывающий: рассылку, заказанную телом запроса, и создание или
/// изменение автоматизаций, которые переживут сессию. Гарантия молчания есть только
/// у <c>read_only</c>.
/// </para>
/// <para>
/// Поиск полей призыва переиспользует <see cref="IssueWritePolicy.FindNotificationField"/>:
/// список полей и правила поиска (любая вложенность, сравнение без учёта регистра) у обеих
/// политик одни и те же, отличается только текст отказа.
/// </para>
/// </remarks>
public static class ExternalEffectsPolicy
{
    /// <summary>
    /// Сегменты пути, обозначающие ресурсы автоматизаций Трекера
    /// (<c>queues/{KEY}/triggers</c>, <c>queues/{KEY}/autoactions</c>).
    /// </summary>
    private static readonly string[] AutomationSegments =
    {
        "triggers",
        "autoactions",
    };

    /// <summary>
    /// Ищет в пути запроса сегмент ресурса автоматизаций.
    /// </summary>
    /// <remarks>
    /// Сегменты сравниваются после percent-декодирования (см. <c>RequestUriPath</c>),
    /// иначе <c>%74riggers</c> проходил бы мимо проверки.
    /// </remarks>
    /// <param name="decodedSegments">Декодированные сегменты пути запроса.</param>
    /// <returns>Каноническое имя найденного сегмента или <c>null</c>, если его нет.</returns>
    public static string? FindAutomationSegment(IEnumerable<string> decodedSegments)
    {
        foreach (var segment in decodedSegments)
        {
            foreach (var automation in AutomationSegments)
            {
                if (string.Equals(segment, automation, StringComparison.OrdinalIgnoreCase))
                {
                    return automation;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Ищет в JSON-теле запроса поля призыва на любом уровне вложенности.
    /// </summary>
    /// <param name="body">Разобранное тело запроса.</param>
    /// <returns>Имя первого найденного поля или <c>null</c>, если таких полей нет.</returns>
    public static string? FindSummonField(JsonElement body) =>
        IssueWritePolicy.FindNotificationField(body);

    /// <summary>
    /// Строит исключение об отказе для тела запроса, заказывающего призыв.
    /// </summary>
    /// <param name="field">Имя поля, например <c>summonees</c>.</param>
    /// <param name="profileName">Имя профиля.</param>
    /// <returns>Исключение с кодом <see cref="ErrorCode.PolicyViolation"/>.</returns>
    public static TrackerException DeniedSummon(string field, string profileName) =>
        new(ErrorCode.PolicyViolation, DeniedSummonMessage(field, profileName));

    /// <summary>
    /// Формирует текст отказа для поля призыва в теле запроса.
    /// </summary>
    /// <param name="field">Имя поля, например <c>summonees</c>.</param>
    /// <param name="profileName">Имя профиля.</param>
    /// <returns>Готовое сообщение об ошибке.</returns>
    public static string DeniedSummonMessage(string field, string profileName) =>
        $"request body field '{field}' summons recipients by mail and is blocked by "
        + $"external_effects of profile '{profileName}'";

    /// <summary>
    /// Строит исключение об отказе для мутации автоматизаций.
    /// </summary>
    /// <param name="segment">Найденный сегмент пути (<c>triggers</c>/<c>autoactions</c>).</param>
    /// <param name="operation">Описание операции, например <c>POST queues/DEV/triggers</c>.</param>
    /// <param name="profileName">Имя профиля.</param>
    /// <returns>Исключение с кодом <see cref="ErrorCode.PolicyViolation"/>.</returns>
    public static TrackerException DeniedAutomation(string segment, string operation, string profileName) =>
        new(ErrorCode.PolicyViolation, DeniedAutomationMessage(segment, operation, profileName));

    /// <summary>
    /// Формирует текст отказа для мутации автоматизаций.
    /// </summary>
    /// <param name="segment">Найденный сегмент пути (<c>triggers</c>/<c>autoactions</c>).</param>
    /// <param name="operation">Описание операции, например <c>POST queues/DEV/triggers</c>.</param>
    /// <param name="profileName">Имя профиля.</param>
    /// <returns>Готовое сообщение об ошибке.</returns>
    public static string DeniedAutomationMessage(string segment, string operation, string profileName) =>
        $"write operation '{operation}' changes '{segment}', which can send mail and call "
        + $"external services on its own, and is blocked by external_effects of profile "
        + $"'{profileName}' (reading them is still allowed)";

    /// <summary>
    /// Строит исключение об отказе для тела, которое невозможно проверить.
    /// </summary>
    /// <param name="reason">Причина, например «is too large».</param>
    /// <param name="profileName">Имя профиля.</param>
    /// <returns>Исключение с кодом <see cref="ErrorCode.PolicyViolation"/>.</returns>
    public static TrackerException DeniedUninspectableBody(string reason, string profileName) =>
        new(
            ErrorCode.PolicyViolation,
            $"request body {reason} and cannot be verified against external_effects "
            + $"of profile '{profileName}'");
}
