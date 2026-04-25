namespace YandexTrackerCLI.Commands.Ref;

using System.CommandLine;

/// <summary>
/// Команда <c>yt ref issue-types</c>: выполняет <c>GET /v3/issuetypes</c>
/// и печатает ответ как есть (JSON-массив типов задач).
/// </summary>
public static class RefIssueTypesCommand
{
    /// <summary>
    /// Строит subcommand <c>issue-types</c> для <c>yt ref</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build() =>
        RefCommandFactory.Build(
            "issue-types",
            "Справочник типов задач (GET /v3/issuetypes).",
            "issuetypes");
}
