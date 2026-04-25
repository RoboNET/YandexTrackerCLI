namespace YandexTrackerCLI.Commands.Board;

using System.CommandLine;

/// <summary>
/// Контейнер группы команд <c>yt board</c>: собирает subtree с подкомандами
/// работы с досками задач (list/get).
/// </summary>
public static class BoardCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>board</c> со всеми подкомандами.
    /// </summary>
    /// <returns>Команда <c>board</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("board", "Доски задач.");
        cmd.Subcommands.Add(BoardListCommand.Build());
        cmd.Subcommands.Add(BoardGetCommand.Build());
        return cmd;
    }
}
