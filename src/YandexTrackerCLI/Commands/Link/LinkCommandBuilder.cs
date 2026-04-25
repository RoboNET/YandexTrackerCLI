namespace YandexTrackerCLI.Commands.Link;

using System.CommandLine;

/// <summary>
/// Контейнер группы команд <c>yt link</c>: собирает subtree с подкомандами
/// работы со связями задачи (list/add/remove).
/// </summary>
public static class LinkCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>link</c> со всеми подкомандами.
    /// </summary>
    /// <returns>Команда <c>link</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("link", "Связи задачи.");
        cmd.Subcommands.Add(LinkListCommand.Build());
        cmd.Subcommands.Add(LinkAddCommand.Build());
        cmd.Subcommands.Add(LinkRemoveCommand.Build());
        return cmd;
    }
}
