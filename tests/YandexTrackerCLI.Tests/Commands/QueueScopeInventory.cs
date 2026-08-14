namespace YandexTrackerCLI.Tests.Commands;

using System.CommandLine;
using YandexTrackerCLI.Commands;

/// <summary>
/// Инвентарь листовых команд CLI, разложенный по отношению к политике профиля
/// <c>allowed_queues</c>. Служит одновременно таблицей покрытия и документацией
/// пределов фильтрации.
/// </summary>
/// <remarks>
/// <para>
/// Что именно считается утечкой: <b>структурная ссылка</b> — объект задачи или очереди
/// с полем <c>key</c> из чужой очереди, попавший в коллекцию, которую печатает команда.
/// Строка <c>OPS-7</c> внутри текста комментария, записи changelog или значения поля
/// разрешённой задачи утечкой не считается: это законное содержимое, которое пользователь
/// написал сам и обязан видеть. Поэтому инвариант нельзя формулировать как «подстроки
/// чужого ключа нет в выводе» — он формулируется по коллекциям, которые печатает команда.
/// </para>
/// <para>
/// Каждая листовая команда обязана лежать ровно в одной из двух таблиц; за этим следит
/// <see cref="QueueScopeCoverageTests"/>. Новая команда, не внесённая ни в одну, роняет
/// сборку тестов — это и есть механическая замена перебору глазами, из-за которого в своё
/// время пропустили <c>suggest</c>.
/// </para>
/// </remarks>
internal static class QueueScopeInventory
{
    /// <summary>
    /// Команды, печатающие коллекции задач или очередей, полученные не по явно названному
    /// пользователем ключу, — либо адресующие ресурс идентификатором, за которым стоит
    /// очередь-владелец. Каждая обязана иметь содержательный тест в
    /// <see cref="QueueScopeLeakTests"/>.
    /// </summary>
    public static readonly IReadOnlySet<string> LeaksIssueCollections =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "issue find",
            "queue list",
            "suggest",
            "link list",
            "component get",
            "component update",
            "component delete",
            "version get",
            "version update",
            "version delete",
        };

    /// <summary>
    /// Команды, не печатающие коллекций задач или очередей вне списка профиля, — с причиной,
    /// объясняющей, почему это безопасно или почему это осознанный предел.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NoIssueCollections =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // --- auth / config: локальное состояние, не данные Трекера -------------------
            ["auth status"] =
                "печатает состояние профиля и действующие политики, а не данные Трекера.",
            ["auth login"] =
                "заводит профиль в локальном конфиге; в Трекер ходит только за проверкой токена (/myself).",
            ["auth relogin"] =
                "повторный браузерный вход federated-профиля; выдачи Трекера не печатает.",
            ["auth logout"] =
                "удаляет профиль из локального конфига; в Трекер не ходит.",
            ["config list"] =
                "читает локальный файл конфигурации.",
            ["config get"] =
                "читает локальный файл конфигурации.",
            ["config set"] =
                "пишет локальный файл конфигурации (ужесточение политики проверяется отдельно).",
            ["config profile"] =
                "переключает default_profile в локальном файле конфигурации.",

            // --- пользователи и справочники: сущностей с ключом очереди нет ---------------
            ["user me"] = "GET /myself — данные текущего пользователя.",
            ["user get"] = "GET /users/{id} — пользователь, не задача.",
            ["user search"] = "GET /users?query= — пользователи, не задачи.",
            ["user list"] = "GET /users — пользователи, не задачи.",
            ["ref statuses"] = "глобальный справочник статусов; задач и очередей не содержит.",
            ["ref priorities"] = "глобальный справочник приоритетов; задач и очередей не содержит.",
            ["ref issue-types"] = "глобальный справочник типов задач; задач и очередей не содержит.",
            ["ref resolutions"] = "глобальный справочник резолюций; задач и очередей не содержит.",
            ["field list"] =
                "глобальные поля (GET /fields) либо локальные поля очереди "
                + "(queues/{queue}/localFields) — во втором случае очередь стоит в URL и её "
                + "проверяет HTTP-guard.",
            ["field get"] =
                "то же, что field list: глобальное поле по id либо локальное поле очереди из URL.",

            // --- задачи, адресованные ключом: очередь стоит в URL -------------------------
            ["issue get"] =
                "ключ задачи назван пользователем и стоит в URL — чужую очередь HTTP-guard "
                + "отклоняет до запроса. ЗАДОКУМЕНТИРОВАННЫЙ ПРЕДЕЛ: ответ печатается как есть, "
                + "поэтому в полях parent/links разрешённой задачи видны не только ключи чужих "
                + "задач, но и их display (тема). Сами чужие задачи остаются нечитаемыми: "
                + "issue get по такому ключу отклоняется. Зафиксировано тестом "
                + "QueueScopeLeakTests.DocumentedLimit_IssueGet_ShowsForeignKeyAndDisplayOfParent.",
            ["issue changelog"] =
                "changelog разрешённой задачи (issues/{key}/changelog → guard). Чужие ключи в "
                + "записях об изменении связей — тот же задокументированный предел, что и "
                + "parent/links.",
            ["issue update"] = "issues/{key} в URL → guard; печатает ту же задачу.",
            ["issue transition"] = "issues/{key}/transitions в URL → guard; печатает переходы той же задачи.",
            ["issue delete"] = "issues/{key} в URL → guard.",
            ["issue create"] =
                "очередь приезжает в теле запроса и проверяется EnsureTargetQueueAllowed "
                + "(fail closed при неразбираемой очереди); печатает созданную задачу своей очереди.",
            ["issue move"] =
                "исходный ключ в URL → guard, целевая очередь из тела → EnsureTargetQueueAllowed.",
            ["issue batch"] =
                "bulkchange адресует задачи телом: форма с query отклоняется целиком, каждый ключ "
                + "из issues проверяется, тело без явного списка задач отклоняется. Печатает "
                + "статус операции, а не список задач.",

            // --- коллекции, принадлежащие одной разрешённой задаче ------------------------
            ["comment list"] =
                "issues/{key}/comments → guard; печатает комментарии разрешённой задачи. Ключ "
                + "чужой задачи в тексте комментария — законное содержимое, вырезать его нельзя.",
            ["comment add"] = "issues/{key}/comments → guard.",
            ["comment update"] = "issues/{key}/comments/{id} → guard.",
            ["comment delete"] = "issues/{key}/comments/{id} → guard.",
            ["worklog list"] =
                "issues/{key}/worklog → guard; записи учёта времени принадлежат самой задаче и "
                + "ссылок на другие задачи не несут.",
            ["worklog add"] = "issues/{key}/worklog → guard.",
            ["worklog update"] = "issues/{key}/worklog/{id} → guard.",
            ["worklog delete"] = "issues/{key}/worklog/{id} → guard.",
            ["attachment list"] = "issues/{key}/attachments → guard; вложения принадлежат самой задаче.",
            ["attachment upload"] = "issues/{key}/attachments → guard.",
            ["attachment download"] =
                "issues/{key}/attachments/{id}/download → guard; печатает байты вложения, не JSON.",
            ["attachment delete"] = "issues/{key}/attachments/{id} → guard.",
            ["checklist get"] =
                "issues/{key}/checklistItems → guard; пункты принадлежат самой задаче. Ссылка на "
                + "другую задачу, поставленная пользователем в пункте, — тот же предел, что "
                + "parent/links: утекает ключ, сама задача остаётся нечитаемой.",
            ["checklist add-item"] = "issues/{key}/checklistItems → guard.",
            ["checklist toggle"] = "issues/{key}/checklistItems → guard.",
            ["checklist update"] = "issues/{key}/checklistItems/{itemId} → guard.",
            ["checklist remove"] = "issues/{key}/checklistItems/{itemId} → guard.",
            ["link add"] =
                "issues/{key}/links → guard по исходной задаче; связываемый ключ едет в теле, "
                + "и связь с чужой задачей мутирует чужую задачу лишь косвенно — на выдачу link list "
                + "она всё равно не попадёт (см. group 1).",
            ["link remove"] = "issues/{key}/links/{id} → guard.",

            // --- автоматизации: очередь обязательна и стоит в URL -------------------------
            ["automation trigger list"] = "queues/{queue}/triggers → guard; правила очереди, не задачи.",
            ["automation trigger get"] = "queues/{queue}/triggers/{id} → guard.",
            ["automation trigger create"] = "queues/{queue}/triggers → guard.",
            ["automation trigger update"] = "queues/{queue}/triggers/{id} → guard.",
            ["automation trigger delete"] = "queues/{queue}/triggers/{id} → guard.",
            ["automation trigger activate"] = "queues/{queue}/triggers/{id}/activate → guard.",
            ["automation trigger deactivate"] = "queues/{queue}/triggers/{id}/deactivate → guard.",
            ["automation autoaction list"] = "queues/{queue}/autoactions → guard; правила очереди, не задачи.",
            ["automation autoaction get"] = "queues/{queue}/autoactions/{id} → guard.",
            ["automation autoaction create"] = "queues/{queue}/autoactions → guard.",
            ["automation autoaction update"] = "queues/{queue}/autoactions/{id} → guard.",
            ["automation autoaction delete"] = "queues/{queue}/autoactions/{id} → guard.",
            ["automation autoaction activate"] = "queues/{queue}/autoactions/{id}/activate → guard.",
            ["automation autoaction deactivate"] = "queues/{queue}/autoactions/{id}/deactivate → guard.",
            ["automation macro list"] = "queues/{queue}/macros → guard; макросы очереди, не задачи.",
            ["automation macro get"] = "queues/{queue}/macros/{id} → guard.",
            ["automation macro create"] = "queues/{queue}/macros → guard.",
            ["automation macro update"] = "queues/{queue}/macros/{id} → guard.",
            ["automation macro delete"] = "queues/{queue}/macros/{id} → guard.",

            // --- компоненты и версии, адресованные очередью -------------------------------
            ["component list"] = "--queue обязателен и стоит в URL queues/{queue}/components → guard.",
            ["component create"] =
                "очередь приезжает в теле запроса и проверяется EnsureTargetQueueAllowed (fail closed).",
            ["version list"] = "--queue обязателен и стоит в URL queues/{queue}/versions → guard.",
            ["version create"] =
                "очередь приезжает в теле запроса и проверяется EnsureTargetQueueAllowed (fail closed).",

            // --- задокументированные пределы: сущности без однозначной очереди -------------
            ["board list"] =
                "ЗАДОКУМЕНТИРОВАННЫЙ ПРЕДЕЛ: доски не фильтруются — доска не привязана к одной "
                + "очереди (её фильтр может охватывать несколько), однозначного владельца нет. "
                + "В ответе — описания досок; задач на доске команда не печатает.",
            ["board get"] =
                "ЗАДОКУМЕНТИРОВАННЫЙ ПРЕДЕЛ: тот же, что board list — доска по id отдаётся как есть, "
                + "задач в ответе нет.",
            ["sprint list"] =
                "ЗАДОКУМЕНТИРОВАННЫЙ ПРЕДЕЛ: спринты не фильтруются — спринт принадлежит доске, "
                + "а не очереди. Ключей задач в ответе нет.",
            ["sprint get"] =
                "ЗАДОКУМЕНТИРОВАННЫЙ ПРЕДЕЛ: тот же, что sprint list.",
            ["project list"] =
                "ЗАДОКУМЕНТИРОВАННЫЙ ПРЕДЕЛ: проекты (entities/project) не фильтруются — проект "
                + "не привязан к очереди. Ключей задач в ответе нет.",
            ["project get"] =
                "ЗАДОКУМЕНТИРОВАННЫЙ ПРЕДЕЛ: тот же, что project list.",
            ["project create"] =
                "ЗАДОКУМЕНТИРОВАННЫЙ ПРЕДЕЛ: проект не привязан к очереди, ограничить создание "
                + "по allowed_queues нечем; печатает созданный проект.",
            ["project update"] =
                "ЗАДОКУМЕНТИРОВАННЫЙ ПРЕДЕЛ: тот же, что project create.",
            ["project delete"] =
                "ЗАДОКУМЕНТИРОВАННЫЙ ПРЕДЕЛ: тот же, что project create.",

            // --- интерактивный fuzzy-поиск -----------------------------------------------
        };

    /// <summary>
    /// Обходит дерево команд, собранное <see cref="RootCommandBuilder.Build"/>, и возвращает
    /// пути всех листьев (команд без подкоманд) вида <c>issue find</c>.
    /// </summary>
    /// <returns>Отсортированный список путей листовых команд.</returns>
    public static IReadOnlyList<string> LeafPaths()
    {
        var sink = new List<string>();
        Walk(RootCommandBuilder.Build(), string.Empty, sink);
        sink.Sort(StringComparer.Ordinal);
        return sink;
    }

    private static void Walk(Command command, string prefix, List<string> sink)
    {
        foreach (var sub in command.Subcommands)
        {
            var path = prefix.Length == 0 ? sub.Name : prefix + " " + sub.Name;
            if (sub.Subcommands.Count == 0)
            {
                sink.Add(path);
            }
            else
            {
                Walk(sub, path, sink);
            }
        }
    }
}
