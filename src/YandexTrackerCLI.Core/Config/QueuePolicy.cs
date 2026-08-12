namespace YandexTrackerCLI.Core.Config;

using Api.Errors;

/// <summary>
/// Политика <c>allowed_queues</c>: нормализация списка очередей профиля, проверка
/// принадлежности очереди списку и построение единообразного сообщения об отказе.
/// </summary>
/// <remarks>
/// Сравнение ключей очередей выполняется <b>без учёта регистра</b>
/// (<see cref="StringComparer.OrdinalIgnoreCase"/>). Ключи очередей в Яндекс Трекере
/// хранятся в верхнем регистре и в URL регистрозависимы, поэтому исходное написание,
/// заданное пользователем, сохраняется как есть (в конфиге и в выводе). Но политика —
/// это защитный механизм: сравнение с учётом регистра означало бы, что запрос к
/// <c>dev-1</c> обходит запись <c>DEV</c> в списке. Поэтому нестрогое сравнение здесь
/// строго безопаснее строгого.
/// </remarks>
public static class QueuePolicy
{
    /// <summary>
    /// Приводит список очередей к каноническому виду: обрезает пробелы, отбрасывает
    /// пустые элементы и удаляет дубликаты без учёта регистра (сохраняя первое написание).
    /// </summary>
    /// <param name="values">Исходные значения; может быть <c>null</c>.</param>
    /// <returns>Нормализованный массив; пустой, если ограничения нет.</returns>
    public static string[] Normalize(IEnumerable<string?>? values)
    {
        if (values is null)
        {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var trimmed = raw.Trim();
            if (seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Разбирает строку со списком очередей через запятую (значение <c>yt config set allowed_queues</c>).
    /// </summary>
    /// <param name="value">Строка вида <c>DEV,OPS,QA</c>; пустая строка снимает ограничение.</param>
    /// <returns>Нормализованный массив очередей; пустой, если ограничения нет.</returns>
    public static string[] ParseList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : Normalize(value.Split(','));

    /// <summary>
    /// Пересекает два уже нормализованных списка без учёта регистра.
    /// </summary>
    /// <remarks>
    /// Написание берётся из <paramref name="first"/>: первым передаётся более авторитетный
    /// список (тот, что задан в профиле), и именно его написание должно попасть в вывод —
    /// второй список приходит от вызывающего, и доверия к его форме меньше.
    /// </remarks>
    /// <param name="first">Первый список; задаёт написание результата.</param>
    /// <param name="second">Второй список.</param>
    /// <returns>Элементы, присутствующие в обоих списках; пустой массив, если пересечения нет.</returns>
    public static string[] Intersect(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        var other = new HashSet<string>(second, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        for (var i = 0; i < first.Count; i++)
        {
            if (other.Contains(first[i]))
            {
                result.Add(first[i]);
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Форматирует список очередей для вывода (<c>yt config get allowed_queues</c>).
    /// </summary>
    /// <param name="values">Список очередей; может быть <c>null</c>.</param>
    /// <returns>Строка вида <c>DEV,QA</c> или пустая строка, если ограничения нет.</returns>
    public static string FormatList(IReadOnlyList<string>? values) =>
        values is null || values.Count == 0 ? string.Empty : string.Join(",", values);

    /// <summary>
    /// Определяет, задано ли профилем ограничение по очередям.
    /// </summary>
    /// <param name="allowed">Список разрешённых очередей.</param>
    /// <returns><c>true</c>, если список непустой.</returns>
    public static bool IsRestricted(IReadOnlyList<string>? allowed) => allowed is { Count: > 0 };

    /// <summary>
    /// Проверяет, разрешено ли обращение к очереди.
    /// </summary>
    /// <param name="allowed">Список разрешённых очередей; <c>null</c>/пустой = ограничения нет.</param>
    /// <param name="queueKey">Ключ очереди, например <c>DEV</c>.</param>
    /// <returns><c>true</c>, если ограничения нет или очередь входит в список.</returns>
    public static bool IsAllowed(IReadOnlyList<string>? allowed, string? queueKey)
    {
        if (!IsRestricted(allowed))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(queueKey))
        {
            // Ограничение задано, но очередь определить не удалось — считаем запрещённым:
            // защитный механизм не должен «раскрываться» на неразобранном вводе.
            return false;
        }

        var key = queueKey.Trim();
        for (var i = 0; i < allowed!.Count; i++)
        {
            if (string.Equals(allowed[i], key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Извлекает ключ очереди из ключа задачи: префикс до первого дефиса.
    /// </summary>
    /// <param name="issueKey">Ключ задачи, например <c>DEV-1</c>.</param>
    /// <returns>Ключ очереди, или <c>null</c>, если строка пустая.</returns>
    public static string? QueueOfIssueKey(string? issueKey)
    {
        if (string.IsNullOrWhiteSpace(issueKey))
        {
            return null;
        }

        var key = issueKey.Trim();
        var dash = key.IndexOf('-');
        // Дефиса нет — это не ключ вида QUEUE-N; возвращаем строку как есть, чтобы
        // вызывающий трактовал её как ключ очереди (и, при действующем ограничении,
        // заблокировал неизвестное значение вместо того, чтобы пропустить его).
        return dash > 0 ? key[..dash] : key;
    }

    /// <summary>
    /// Строит исключение об отказе по политике профиля.
    /// </summary>
    /// <param name="queueKey">Очередь, к которой произошло обращение.</param>
    /// <param name="profileName">Имя профиля.</param>
    /// <param name="allowed">Действующий список разрешённых очередей.</param>
    /// <returns>Исключение с кодом <see cref="ErrorCode.PolicyViolation"/>.</returns>
    public static TrackerException Denied(
        string? queueKey,
        string profileName,
        IReadOnlyList<string>? allowed) =>
        new(ErrorCode.PolicyViolation, DeniedMessage(queueKey, profileName, allowed));

    /// <summary>
    /// Формирует текст отказа: <c>queue 'OPS' is outside allowed_queues of profile 'ci' (allowed: DEV, QA)</c>.
    /// </summary>
    /// <param name="queueKey">Очередь, к которой произошло обращение.</param>
    /// <param name="profileName">Имя профиля.</param>
    /// <param name="allowed">Действующий список разрешённых очередей.</param>
    /// <returns>Готовое сообщение об ошибке.</returns>
    public static string DeniedMessage(
        string? queueKey,
        string profileName,
        IReadOnlyList<string>? allowed)
    {
        var queue = string.IsNullOrWhiteSpace(queueKey) ? "<unknown>" : queueKey.Trim();
        var list = allowed is null || allowed.Count == 0 ? "<none>" : string.Join(", ", allowed);
        return $"queue '{queue}' is outside allowed_queues of profile '{profileName}' (allowed: {list})";
    }
}
