namespace YandexTrackerCLI.Commands.Automation;

using System.CommandLine;
using System.Globalization;

/// <summary>
/// Общий флаг <c>--version</c> для PATCH-команд автоматизаций
/// (<c>update</c>/<c>activate</c>/<c>deactivate</c> у триггеров и автодействий).
/// API требует версию (или заголовок <c>If-Match</c>) на любом PATCH и принимает её
/// только query-параметром, поэтому значение дописывается к пути, а не в тело.
/// </summary>
public static class AutomationVersionOption
{
    /// <summary>
    /// Создаёт опцию <c>--version</c> с описанием под конкретный ресурс.
    /// </summary>
    /// <param name="noun">Название ресурса в родительном падеже («триггера», «автодействия»).</param>
    /// <returns>Опциональный (не required) флаг версии.</returns>
    public static Option<long?> Create(string noun) => new("--version")
    {
        Description = $"Версия {noun} для optimistic locking (из предыдущего get); "
                      + "API требует её для PATCH.",
    };

    /// <summary>
    /// Дописывает <c>?version={version}</c> к пути запроса, если версия задана.
    /// </summary>
    /// <param name="path">Путь запроса без query-части.</param>
    /// <param name="version">Версия ресурса либо <c>null</c>, если пользователь её не задал.</param>
    /// <returns>Путь с query-параметром версии либо исходный путь.</returns>
    public static string AppendVersionQuery(string path, long? version) =>
        version.HasValue
            ? path + "?version=" + version.Value.ToString(CultureInfo.InvariantCulture)
            : path;
}
