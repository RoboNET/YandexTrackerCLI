namespace YandexTrackerCLI.Commands.Ref;

using System.CommandLine;

/// <summary>
/// Команда <c>yt ref statuses</c>: выполняет <c>GET /v3/statuses</c>
/// и печатает ответ как есть (JSON-массив статусов задач).
/// </summary>
public static class RefStatusesCommand
{
    /// <summary>
    /// Строит subcommand <c>statuses</c> для <c>yt ref</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build() =>
        RefCommandFactory.Build(
            "statuses",
            "Справочник статусов задач (GET /v3/statuses).",
            "statuses");
}
