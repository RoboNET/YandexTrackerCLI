namespace YandexTrackerCLI.Commands.Sprint;

using System.CommandLine;

/// <summary>
/// Контейнер группы команд <c>yt sprint</c>: собирает subtree с подкомандами
/// работы со спринтами (list/get).
/// </summary>
public static class SprintCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>sprint</c> со всеми подкомандами.
    /// </summary>
    /// <returns>Команда <c>sprint</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("sprint", "Спринты.");
        cmd.Subcommands.Add(SprintListCommand.Build());
        cmd.Subcommands.Add(SprintGetCommand.Build());
        return cmd;
    }
}
