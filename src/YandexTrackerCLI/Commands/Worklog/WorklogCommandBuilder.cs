namespace YandexTrackerCLI.Commands.Worklog;

using System.CommandLine;

/// <summary>
/// Контейнер группы команд <c>yt worklog</c>: собирает subtree с подкомандами
/// учёта времени по задачам.
/// </summary>
public static class WorklogCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>worklog</c> со всеми подкомандами.
    /// </summary>
    /// <returns>Команда <c>worklog</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("worklog", "Учёт времени по задачам.");
        cmd.Subcommands.Add(WorklogListCommand.Build());
        cmd.Subcommands.Add(WorklogAddCommand.Build());
        cmd.Subcommands.Add(WorklogUpdateCommand.Build());
        cmd.Subcommands.Add(WorklogDeleteCommand.Build());
        return cmd;
    }
}
