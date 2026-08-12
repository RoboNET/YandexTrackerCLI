namespace YandexTrackerCLI.Core.Config;

using System.Text.Json;
using Api.Errors;

/// <summary>
/// Политика <c>allowed_write_issues</c>: список ключей задач, в которые профилю разрешена
/// <b>запись</b>. Чтение политикой не ограничивается — этим она отличается от <c>read_only</c>.
/// </summary>
/// <remarks>
/// <para>
/// Пломбирование списка (нормализация, разбор строки через запятую, форматирование,
/// регистронезависимое сравнение) переиспользует <see cref="QueuePolicy"/>: правила там
/// ровно те же, отличается только смысл элементов (ключ задачи вместо ключа очереди) и
/// текст отказа.
/// </para>
/// <para>
/// Принцип проверки — <b>запрет по умолчанию</b>: мутирующий запрос проходит только если
/// доказано, что он адресован задаче из списка. Всё, про что этого доказать нельзя
/// (создание задачи, <c>bulkchange</c>, мутации очередей и автоматизаций), запрещено.
/// </para>
/// </remarks>
public static class IssueWritePolicy
{
    /// <summary>
    /// Поля тела запроса, рассылающие уведомления и письма адресатам за пределами задачи.
    /// При действующей политике они запрещены: область записи ограничена задачей, а
    /// призыв и рассылка по списку адресов из неё выходят.
    /// </summary>
    private static readonly string[] NotificationFields =
    {
        "summonees",
        "maillistSummonees",
    };

    /// <summary>
    /// Разбирает строку со списком ключей задач через запятую
    /// (значение <c>yt config set allowed_write_issues</c> или <c>YT_ALLOWED_WRITE_ISSUES</c>).
    /// </summary>
    /// <param name="value">Строка вида <c>DEV-42,DEV-43</c>; пустая строка снимает ограничение.</param>
    /// <returns>Нормализованный массив ключей; пустой, если ограничения нет.</returns>
    public static string[] ParseList(string? value) => QueuePolicy.ParseList(value);

    /// <summary>
    /// Приводит список ключей задач к каноническому виду: обрезает пробелы, отбрасывает
    /// пустые элементы и удаляет дубликаты без учёта регистра.
    /// </summary>
    /// <param name="values">Исходные значения; может быть <c>null</c>.</param>
    /// <returns>Нормализованный массив; пустой, если ограничения нет.</returns>
    public static string[] Normalize(IEnumerable<string?>? values) => QueuePolicy.Normalize(values);

    /// <summary>
    /// Пересекает список профиля со списком из <c>YT_ALLOWED_WRITE_ISSUES</c>: окружение
    /// может область записи только <b>сузить</b>.
    /// </summary>
    /// <param name="profileList">Нормализованный список профиля; задаёт написание результата.</param>
    /// <param name="envList">Нормализованный список из переменной окружения.</param>
    /// <returns>Задачи, разрешённые обоими списками; пустой массив, если пересечения нет.</returns>
    public static string[] Intersect(IReadOnlyList<string> profileList, IReadOnlyList<string> envList) =>
        QueuePolicy.Intersect(profileList, envList);

    /// <summary>
    /// Форматирует список ключей задач для вывода (<c>yt config get allowed_write_issues</c>).
    /// </summary>
    /// <param name="values">Список ключей; может быть <c>null</c>.</param>
    /// <returns>Строка вида <c>DEV-42,DEV-43</c> или пустая строка, если ограничения нет.</returns>
    public static string FormatList(IReadOnlyList<string>? values) => QueuePolicy.FormatList(values);

    /// <summary>
    /// Определяет, задано ли ограничение области записи.
    /// </summary>
    /// <param name="allowed">Список разрешённых на запись задач.</param>
    /// <returns><c>true</c>, если список непустой.</returns>
    public static bool IsRestricted(IReadOnlyList<string>? allowed) => allowed is { Count: > 0 };

    /// <summary>
    /// Проверяет, разрешена ли запись в задачу.
    /// </summary>
    /// <param name="allowed">Список разрешённых задач; <c>null</c>/пустой = ограничения нет.</param>
    /// <param name="issueKey">Ключ задачи, например <c>DEV-42</c>.</param>
    /// <returns>
    /// <c>true</c>, если ограничения нет или ключ входит в список (сравнение без учёта регистра).
    /// Неопределённый ключ при действующем ограничении — <c>false</c>.
    /// </returns>
    public static bool IsAllowed(IReadOnlyList<string>? allowed, string? issueKey) =>
        QueuePolicy.IsAllowed(allowed, issueKey);

    /// <summary>
    /// Строит исключение об отказе записи в конкретную задачу.
    /// </summary>
    /// <param name="issueKey">Ключ задачи, к которой произошло обращение.</param>
    /// <param name="profileName">Имя профиля.</param>
    /// <param name="allowed">Действующий список разрешённых задач.</param>
    /// <returns>Исключение с кодом <see cref="ErrorCode.PolicyViolation"/>.</returns>
    public static TrackerException Denied(
        string? issueKey,
        string profileName,
        IReadOnlyList<string>? allowed) =>
        new(ErrorCode.PolicyViolation, DeniedMessage(issueKey, profileName, allowed));

    /// <summary>
    /// Формирует текст отказа:
    /// <c>issue 'OPS-7' is outside allowed_write_issues of profile 'ci' (allowed: DEV-42)</c>.
    /// </summary>
    /// <param name="issueKey">Ключ задачи, к которой произошло обращение.</param>
    /// <param name="profileName">Имя профиля.</param>
    /// <param name="allowed">Действующий список разрешённых задач.</param>
    /// <returns>Готовое сообщение об ошибке.</returns>
    public static string DeniedMessage(
        string? issueKey,
        string profileName,
        IReadOnlyList<string>? allowed)
    {
        var issue = string.IsNullOrWhiteSpace(issueKey) ? "<unknown>" : issueKey.Trim();
        return $"issue '{issue}' is outside allowed_write_issues of profile '{profileName}' "
               + $"(allowed: {FormatAllowed(allowed)})";
    }

    /// <summary>
    /// Строит исключение об отказе для мутирующего запроса, не привязанного к конкретной
    /// разрешённой задаче (создание задачи, <c>bulkchange</c>, мутации очереди и т. п.).
    /// </summary>
    /// <param name="operation">Описание операции, например <c>POST issues</c>.</param>
    /// <param name="profileName">Имя профиля.</param>
    /// <param name="allowed">Действующий список разрешённых задач.</param>
    /// <returns>Исключение с кодом <see cref="ErrorCode.PolicyViolation"/>.</returns>
    public static TrackerException DeniedUnscoped(
        string operation,
        string profileName,
        IReadOnlyList<string>? allowed) =>
        new(ErrorCode.PolicyViolation, DeniedUnscopedMessage(operation, profileName, allowed));

    /// <summary>
    /// Формирует текст отказа для операции без доказуемой привязки к разрешённой задаче.
    /// </summary>
    /// <param name="operation">Описание операции, например <c>POST issues</c>.</param>
    /// <param name="profileName">Имя профиля.</param>
    /// <param name="allowed">Действующий список разрешённых задач.</param>
    /// <returns>Готовое сообщение об ошибке.</returns>
    public static string DeniedUnscopedMessage(
        string operation,
        string profileName,
        IReadOnlyList<string>? allowed) =>
        $"write operation '{operation}' is not scoped to a single issue and is blocked by "
        + $"allowed_write_issues of profile '{profileName}' (allowed: {FormatAllowed(allowed)})";

    /// <summary>
    /// Строит исключение об отказе для тела запроса, содержащего поле рассылки уведомлений.
    /// </summary>
    /// <param name="field">Имя поля, например <c>summonees</c>.</param>
    /// <param name="profileName">Имя профиля.</param>
    /// <returns>Исключение с кодом <see cref="ErrorCode.PolicyViolation"/>.</returns>
    public static TrackerException DeniedNotificationField(string field, string profileName) =>
        new(ErrorCode.PolicyViolation, DeniedNotificationFieldMessage(field, profileName));

    /// <summary>
    /// Формирует текст отказа для поля рассылки уведомлений в теле запроса.
    /// </summary>
    /// <param name="field">Имя поля, например <c>summonees</c>.</param>
    /// <param name="profileName">Имя профиля.</param>
    /// <returns>Готовое сообщение об ошибке.</returns>
    public static string DeniedNotificationFieldMessage(string field, string profileName) =>
        $"request body field '{field}' notifies recipients outside the issue and is blocked by "
        + $"allowed_write_issues of profile '{profileName}'";

    /// <summary>
    /// Ищет в JSON-теле запроса поля рассылки уведомлений (<c>summonees</c>,
    /// <c>maillistSummonees</c>) на любом уровне вложенности.
    /// </summary>
    /// <remarks>
    /// Поиск рекурсивный, потому что такие поля приходят не только в теле комментария:
    /// например, выполнение перехода принимает вложенный объект комментария. Совпадение
    /// имени поля — без учёта регистра, чтобы вариант написания не давал обхода.
    /// </remarks>
    /// <param name="body">Разобранное тело запроса.</param>
    /// <returns>Имя первого найденного поля или <c>null</c>, если таких полей нет.</returns>
    public static string? FindNotificationField(JsonElement body)
    {
        switch (body.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in body.EnumerateObject())
                {
                    foreach (var field in NotificationFields)
                    {
                        if (string.Equals(property.Name, field, StringComparison.OrdinalIgnoreCase))
                        {
                            // Возвращаем каноническое написание поля, а не пришедшее в теле:
                            // сообщение об ошибке должно указывать на поле API.
                            return field;
                        }
                    }

                    var nested = FindNotificationField(property.Value);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }

                return null;

            case JsonValueKind.Array:
                foreach (var item in body.EnumerateArray())
                {
                    var nested = FindNotificationField(item);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static string FormatAllowed(IReadOnlyList<string>? allowed) =>
        allowed is null || allowed.Count == 0 ? "<none>" : string.Join(", ", allowed);
}
