namespace YandexTrackerCLI.Tests.Commands;

using TUnit.Core;

/// <summary>
/// Проверка полноты классификации команд относительно политики <c>allowed_queues</c>:
/// каждый лист дерева команд обязан лежать ровно в одной из двух таблиц
/// <see cref="QueueScopeInventory"/>, а сами таблицы — не содержать записей, которых
/// в дереве уже нет.
/// </summary>
/// <remarks>
/// Это и есть тот механический инвариант, ради которого всё затевалось: перебор глазами
/// доказывает наличие, но не отсутствие. Новая команда, добавленная в дерево, роняет эти
/// тесты до тех пор, пока автор не примет решение — фильтровать её (с содержательным
/// тестом) или явно записать причину, почему фильтровать нечего.
/// Тесты чистые: HTTP и Console не трогают, поэтому параллелизм допустим.
/// </remarks>
public sealed class QueueScopeCoverageTests
{
    /// <summary>
    /// Каждый лист дерева команд классифицирован хотя бы в одной таблице.
    /// </summary>
    [Test]
    public async Task EveryLeafCommand_IsClassified()
    {
        var unclassified = QueueScopeInventory.LeafPaths()
            .Where(p => !QueueScopeInventory.LeaksIssueCollections.Contains(p)
                        && !QueueScopeInventory.NoIssueCollections.ContainsKey(p))
            .ToArray();

        var report = unclassified.Length == 0
            ? string.Empty
            : "команды добавлены, но не классифицированы относительно allowed_queues: "
              + string.Join(", ", unclassified)
              + ". Внесите каждую в QueueScopeInventory.LeaksIssueCollections "
              + "(и добавьте содержательный тест в QueueScopeLeakTests) либо в "
              + "QueueScopeInventory.NoIssueCollections с причиной, почему коллекций задач "
              + "и очередей команда не печатает.";

        await Assert.That(report).IsEmpty();
    }

    /// <summary>
    /// Ни один лист не попал в обе таблицы сразу: группа должна быть выбрана, а не «и то, и то».
    /// </summary>
    [Test]
    public async Task NoLeafCommand_IsClassifiedTwice()
    {
        var both = QueueScopeInventory.LeaksIssueCollections
            .Where(QueueScopeInventory.NoIssueCollections.ContainsKey)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        var report = both.Length == 0
            ? string.Empty
            : "команды классифицированы дважды: " + string.Join(", ", both)
              + ". Оставьте запись ровно в одной таблице QueueScopeInventory.";

        await Assert.That(report).IsEmpty();
    }

    /// <summary>
    /// В таблицах нет записей, которым в дереве команд уже ничего не соответствует, —
    /// иначе классификация протухает и начинает описывать несуществующий CLI.
    /// </summary>
    [Test]
    public async Task NoClassificationEntry_IsStale()
    {
        var leaves = QueueScopeInventory.LeafPaths().ToHashSet(StringComparer.Ordinal);
        var stale = QueueScopeInventory.LeaksIssueCollections
            .Concat(QueueScopeInventory.NoIssueCollections.Keys)
            .Where(p => !leaves.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        var report = stale.Length == 0
            ? string.Empty
            : "в таблицах QueueScopeInventory остались команды, которых нет в дереве: "
              + string.Join(", ", stale)
              + ". Удалите записи или исправьте путь (переименование команды тоже сюда попадает).";

        await Assert.That(report).IsEmpty();
    }

    /// <summary>
    /// Причина во второй таблице обязана быть содержательной: пустая строка или отписка
    /// «не относится» лишают таблицу смысла документации.
    /// </summary>
    [Test]
    public async Task EveryExemption_HasReason()
    {
        var empty = QueueScopeInventory.NoIssueCollections
            .Where(kv => kv.Value.Trim().Length < 20)
            .Select(kv => kv.Key)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        var report = empty.Length == 0
            ? string.Empty
            : "причина исключения не объясняет, почему это безопасно: " + string.Join(", ", empty);

        await Assert.That(report).IsEmpty();
    }
}
