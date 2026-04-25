namespace YandexTrackerCLI.Commands.Ref;

using System.CommandLine;

/// <summary>
/// Команда <c>yt ref resolutions</c>: выполняет <c>GET /v3/resolutions</c>
/// и печатает ответ как есть (JSON-массив резолюций задач).
/// </summary>
public static class RefResolutionsCommand
{
    /// <summary>
    /// Строит subcommand <c>resolutions</c> для <c>yt ref</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build() =>
        RefCommandFactory.Build(
            "resolutions",
            "Справочник резолюций задач (GET /v3/resolutions).",
            "resolutions");
}
