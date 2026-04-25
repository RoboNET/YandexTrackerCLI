namespace YandexTrackerCLI.Commands.Ref;

using System.CommandLine;

/// <summary>
/// Контейнер группы команд <c>yt ref</c>. Регистрирует read-only подкоманды-справочники
/// (статусы, приоритеты, типы, резолюции задач).
/// </summary>
public static class RefCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>ref</c> со всеми подкомандами-справочниками.
    /// </summary>
    /// <returns>Команда <c>ref</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("ref", "Справочники: статусы, приоритеты, типы, резолюции.");
        cmd.Subcommands.Add(RefStatusesCommand.Build());
        cmd.Subcommands.Add(RefPrioritiesCommand.Build());
        cmd.Subcommands.Add(RefIssueTypesCommand.Build());
        cmd.Subcommands.Add(RefResolutionsCommand.Build());
        return cmd;
    }
}
