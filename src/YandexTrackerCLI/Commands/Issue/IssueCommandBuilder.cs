namespace YandexTrackerCLI.Commands.Issue;

using System.CommandLine;

/// <summary>
/// Контейнер группы команд <c>yt issue</c>. Регистрирует все подкоманды
/// работы с задачами (get, find, create, update, transition, move, delete, batch).
/// </summary>
public static class IssueCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>issue</c> со всеми подкомандами.
    /// </summary>
    /// <returns>Команда <c>issue</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("issue", "Работа с задачами (issues).");
        cmd.Subcommands.Add(IssueGetCommand.Build());
        cmd.Subcommands.Add(IssueFindCommand.Build());
        cmd.Subcommands.Add(IssueCreateCommand.Build());
        cmd.Subcommands.Add(IssueUpdateCommand.Build());
        cmd.Subcommands.Add(IssueTransitionCommand.Build());
        cmd.Subcommands.Add(IssueMoveCommand.Build());
        cmd.Subcommands.Add(IssueDeleteCommand.Build());
        cmd.Subcommands.Add(IssueBatchCommand.Build());
        return cmd;
    }
}
