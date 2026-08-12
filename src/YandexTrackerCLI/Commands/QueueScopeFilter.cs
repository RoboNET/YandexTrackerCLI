namespace YandexTrackerCLI.Commands;

using System.Text.Json;
using Core.Api;
using Core.Api.Errors;
using Core.Config;

/// <summary>
/// Пост-фильтрация выдачи по политике профиля <c>allowed_queues</c> на уровне CLI.
/// </summary>
/// <remarks>
/// HTTP-guard (<see cref="Core.Http.AllowedQueuesGuardHandler"/>) блокирует адресные
/// обращения к чужой очереди, но не может ограничить произвольный поиск: YQL-запрос
/// в теле <c>POST /issues/_search</c> сервер выполняет целиком. Поэтому результаты
/// поиска и списки очередей фильтруются здесь — запрос остаётся рабочим, а элементы
/// вне списка просто не попадают в вывод.
/// </remarks>
internal static class QueueScopeFilter
{
    /// <summary>
    /// Проверяет, что элемент выдачи (задача) относится к разрешённой очереди.
    /// Очередь определяется по полю <c>key</c> вида <c>DEV-1</c>.
    /// </summary>
    /// <param name="element">Элемент результата поиска.</param>
    /// <param name="allowed">Список разрешённых очередей; пустой = без ограничения.</param>
    /// <returns><c>true</c>, если элемент допустим к выводу.</returns>
    public static bool AllowsIssue(JsonElement element, IReadOnlyList<string>? allowed)
    {
        if (!QueuePolicy.IsRestricted(allowed))
        {
            return true;
        }

        return QueuePolicy.IsAllowed(allowed, QueuePolicy.QueueOfIssueKey(ReadKey(element)));
    }

    /// <summary>
    /// Проверяет, что элемент выдачи (очередь) входит в разрешённый список.
    /// Очередь определяется по полю <c>key</c> вида <c>DEV</c>.
    /// </summary>
    /// <param name="element">Элемент списка очередей.</param>
    /// <param name="allowed">Список разрешённых очередей; пустой = без ограничения.</param>
    /// <returns><c>true</c>, если элемент допустим к выводу.</returns>
    public static bool AllowsQueue(JsonElement element, IReadOnlyList<string>? allowed)
    {
        if (!QueuePolicy.IsRestricted(allowed))
        {
            return true;
        }

        return QueuePolicy.IsAllowed(allowed, ReadKey(element));
    }

    /// <summary>
    /// Бросает <see cref="Core.Api.Errors.ErrorCode.PolicyViolation"/>, если очередь,
    /// заданная пользователем явно (флагом или полем тела запроса), вне списка профиля.
    /// </summary>
    /// <param name="queueKey">Ключ очереди; <c>null</c>/пустой — проверка пропускается.</param>
    /// <param name="profile">Действующий профиль.</param>
    /// <exception cref="Core.Api.Errors.TrackerException">Очередь вне <c>allowed_queues</c>.</exception>
    public static void EnsureQueueAllowed(string? queueKey, EffectiveProfile profile)
    {
        if (string.IsNullOrWhiteSpace(queueKey) || !QueuePolicy.IsRestricted(profile.AllowedQueues))
        {
            return;
        }

        if (!QueuePolicy.IsAllowed(profile.AllowedQueues, queueKey))
        {
            throw QueuePolicy.Denied(queueKey, profile.Name, profile.AllowedQueues);
        }
    }

    /// <summary>
    /// Строгая проверка целевой очереди мутирующей операции (создание задачи, перенос):
    /// при действующем ограничении неопределимая очередь тоже считается запрещённой.
    /// </summary>
    /// <param name="queueKey">Ключ очереди из тела запроса; <c>null</c>, если разобрать не удалось.</param>
    /// <param name="profile">Действующий профиль.</param>
    /// <exception cref="Core.Api.Errors.TrackerException">
    /// Очередь вне <c>allowed_queues</c> или не поддаётся разбору при действующем ограничении.
    /// </exception>
    public static void EnsureTargetQueueAllowed(string? queueKey, EffectiveProfile profile)
    {
        if (!QueuePolicy.IsRestricted(profile.AllowedQueues))
        {
            return;
        }

        if (!QueuePolicy.IsAllowed(profile.AllowedQueues, queueKey))
        {
            throw QueuePolicy.Denied(queueKey, profile.Name, profile.AllowedQueues);
        }
    }

    /// <summary>
    /// Извлекает ключ очереди из значения поля <c>queue</c> тела запроса: строка
    /// (<c>"DEV"</c>) либо объект с полем <c>key</c> (<c>{"key":"DEV"}</c>).
    /// </summary>
    /// <param name="queueValue">Значение поля <c>queue</c>.</param>
    /// <returns>Ключ очереди или <c>null</c>, если значение задано идентификатором.</returns>
    public static string? ReadQueueKeyFromJson(JsonElement queueValue) => queueValue.ValueKind switch
    {
        JsonValueKind.String => queueValue.GetString(),
        JsonValueKind.Object => ReadKey(queueValue),
        _ => null,
    };

    /// <summary>
    /// Бросает <see cref="Core.Api.Errors.ErrorCode.PolicyViolation"/>, если очередь
    /// ключа задачи вне списка профиля.
    /// </summary>
    /// <param name="issueKey">Ключ задачи вида <c>DEV-1</c>.</param>
    /// <param name="profile">Действующий профиль.</param>
    /// <exception cref="Core.Api.Errors.TrackerException">Очередь вне <c>allowed_queues</c>.</exception>
    public static void EnsureIssueKeyAllowed(string? issueKey, EffectiveProfile profile) =>
        EnsureQueueAllowed(QueuePolicy.QueueOfIssueKey(issueKey), profile);

    /// <summary>
    /// Вырезает из ответа <c>GET /v3/issues/{key}/links</c> связи с задачами вне
    /// <c>allowed_queues</c>.
    /// </summary>
    /// <remarks>
    /// Сам запрос адресован разрешённой задаче, поэтому HTTP-guard его пропускает, но тело
    /// ответа перечисляет связанные задачи любых очередей — с ключами, темами и статусами.
    /// Очередь связи определяется по <c>object.key</c>; связь, у которой ключ не разбирается,
    /// при действующем ограничении отбрасывается (<i>fail closed</i>).
    /// </remarks>
    /// <param name="payload">Ответ API (ожидается массив связей).</param>
    /// <param name="allowed">Разрешённые очереди; пустой список = без ограничения.</param>
    /// <returns>
    /// Документ с отфильтрованным массивом (владеет памятью — вызывающий обязан его освободить),
    /// либо <c>null</c>, если фильтровать нечего и можно печатать исходный ответ.
    /// </returns>
    public static JsonDocument? FilterLinkArray(JsonElement payload, IReadOnlyList<string>? allowed)
    {
        if (!QueuePolicy.IsRestricted(allowed) || payload.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartArray();
            foreach (var link in payload.EnumerateArray())
            {
                var target = link.ValueKind == JsonValueKind.Object
                             && link.TryGetProperty("object", out var obj)
                    ? ReadKey(obj)
                    : null;

                if (QueuePolicy.IsAllowed(allowed, QueuePolicy.QueueOfIssueKey(target)))
                {
                    link.WriteTo(w);
                }
            }
            w.WriteEndArray();
        }

        return JsonDocument.Parse(ms.ToArray());
    }

    /// <summary>
    /// Проверяет политику для ресурса, адресуемого по идентификатору, а не по очереди
    /// (<c>components/{id}</c>, <c>versions/{id}</c>): доспрашивает ресурс и сверяет его
    /// очередь-владельца с <c>allowed_queues</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Резолв живёт в слое команды, а не в HTTP-guard: guard видит только тот запрос,
    /// который вот-вот уйдёт, и не должен сам ходить в сеть — доп. запрос из хендлера
    /// проходил бы по той же цепочке и рисковал рекурсией. Команда же знает, что за
    /// ресурс адресует, и делает ровно один дополнительный <c>GET</c>.
    /// </para>
    /// <para>
    /// Roundtrip выполняется только при действующем ограничении; без него метод не делает
    /// ни одного запроса.
    /// </para>
    /// <para>Поведение краевых случаев:</para>
    /// <list type="bullet">
    ///   <item><description>ресурса нет — <c>GET</c> бросает обычный <c>not_found</c> (exit 5);
    ///   политика не подменяет собой отсутствие ресурса;</description></item>
    ///   <item><description>в ответе нет разбираемой очереди — отказ по политике
    ///   (<i>fail closed</i>): доказать принадлежность разрешённой очереди не удалось.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="client">Клиент Трекера.</param>
    /// <param name="resource">Имя коллекции: <c>components</c> или <c>versions</c>.</param>
    /// <param name="id">Идентификатор ресурса.</param>
    /// <param name="profile">Действующий профиль.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>
    /// Полученное представление ресурса, если доспрос выполнялся (команды чтения
    /// переиспользуют его вместо повторного <c>GET</c>), либо <c>null</c>, если ограничения нет.
    /// </returns>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.PolicyViolation"/> — очередь-владелец вне списка или не определяется;
    /// прочие коды (например <see cref="ErrorCode.NotFound"/>) проходят от доспроса как есть.
    /// </exception>
    public static async Task<JsonElement?> EnsureResourceQueueAllowed(
        TrackerClient client,
        string resource,
        string id,
        EffectiveProfile profile,
        CancellationToken ct)
    {
        if (!QueuePolicy.IsRestricted(profile.AllowedQueues))
        {
            return null;
        }

        var resolved = await client.GetAsync($"{resource}/{Uri.EscapeDataString(id)}", ct);
        var queue = resolved.ValueKind == JsonValueKind.Object
                    && resolved.TryGetProperty("queue", out var queueEl)
            ? ReadQueueKeyFromJson(queueEl)
            : null;

        if (string.IsNullOrWhiteSpace(queue))
        {
            throw new TrackerException(
                ErrorCode.PolicyViolation,
                $"could not determine the owning queue of {resource}/{id}; "
                + $"blocked by allowed_queues of profile '{profile.Name}' "
                + $"(allowed: {string.Join(", ", profile.AllowedQueues!)})");
        }

        EnsureTargetQueueAllowed(queue, profile);
        return resolved;
    }

    private static string? ReadKey(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty("key", out var key)
        && key.ValueKind == JsonValueKind.String
            ? key.GetString()
            : null;
}
