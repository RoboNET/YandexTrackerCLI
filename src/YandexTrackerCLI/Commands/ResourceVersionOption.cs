namespace YandexTrackerCLI.Commands;

using System.CommandLine;
using System.Globalization;

/// <summary>
/// Общие флаги оптимистической блокировки для PATCH-команд: <c>--version</c>,
/// <c>--no-version-check</c>, <c>--overwrite-latest</c>, — плюс сборка query-параметра
/// <c>?version=N</c>.
/// </summary>
/// <remarks>
/// API принимает версию ресурса только query-параметром (либо заголовком <c>If-Match</c>),
/// поэтому значение дописывается к пути, а не в тело. Для автоматизаций версия обязательна
/// на любом PATCH; для задач — опциональна и подставляется из
/// <see cref="Core.Storage.ResourceVersionCache"/>, если ресурс до этого читали через <c>get</c>.
/// </remarks>
public static class ResourceVersionOption
{
    /// <summary>
    /// Создаёт опцию <c>--version</c> с описанием под конкретный ресурс.
    /// </summary>
    /// <param name="noun">Название ресурса в родительном падеже («задачи», «триггера», «автодействия»).</param>
    /// <returns>Опциональный (не required) флаг версии.</returns>
    public static Option<long?> Create(string noun) => new("--version")
    {
        Description = $"Версия {noun} для optimistic locking (из предыдущего get); "
                      + "переопределяет запомненную версию.",
    };

    /// <summary>
    /// Создаёт опцию <c>--no-version-check</c>: не подставлять версию из кэша.
    /// </summary>
    /// <returns>Флаг отказа от запомненной версии.</returns>
    /// <remarks>
    /// Отключает только кэш. Явный <c>--version</c> и корневое поле <c>version</c> в теле
    /// запроса пользователь задал сам, и молча их игнорировать флаг с таким именем не вправе.
    /// </remarks>
    public static Option<bool> CreateNoVersionCheck() => new("--no-version-check")
    {
        Description = "Не подставлять версию, запомненную предыдущим get; отправить PATCH без проверки версии.",
    };

    /// <summary>
    /// Создаёт опцию <c>--overwrite-latest</c>: перечитать ресурс и записать поверх текущей версии.
    /// </summary>
    /// <param name="noun">Название ресурса в винительном падеже («задачу», «триггер», «автодействие»).</param>
    /// <returns>Флаг осознанной перезаписи чужой правки.</returns>
    public static Option<bool> CreateOverwriteLatest(string noun) => new("--overwrite-latest")
    {
        Description = $"Перечитать {noun} и записать поверх текущей версии, "
                      + "осознанно затирая правки, сделанные после вашего get.",
    };

    /// <summary>
    /// Дописывает <c>?version={version}</c> к пути запроса, если версия задана.
    /// </summary>
    /// <param name="path">Путь запроса без query-части.</param>
    /// <param name="version">Версия ресурса либо <c>null</c>, если версии нет.</param>
    /// <returns>Путь с query-параметром версии либо исходный путь.</returns>
    public static string AppendVersionQuery(string path, long? version) =>
        version.HasValue
            ? path + "?version=" + version.Value.ToString(CultureInfo.InvariantCulture)
            : path;
}
