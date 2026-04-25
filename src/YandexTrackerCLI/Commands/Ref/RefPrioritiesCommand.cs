namespace YandexTrackerCLI.Commands.Ref;

using System.CommandLine;

/// <summary>
/// Команда <c>yt ref priorities</c>: выполняет <c>GET /v3/priorities</c>
/// и печатает ответ как есть (JSON-массив приоритетов задач).
/// </summary>
public static class RefPrioritiesCommand
{
    /// <summary>
    /// Строит subcommand <c>priorities</c> для <c>yt ref</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build() =>
        RefCommandFactory.Build(
            "priorities",
            "Справочник приоритетов задач (GET /v3/priorities).",
            "priorities");
}
