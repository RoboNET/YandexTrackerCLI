namespace YandexTrackerCLI.Commands.Checklist;

using System.CommandLine;

/// <summary>
/// Контейнер группы команд <c>yt checklist</c>: собирает subtree с подкомандами
/// работы с чек-листом задачи (get/add-item/toggle/update/remove).
/// </summary>
public static class ChecklistCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>checklist</c> со всеми подкомандами.
    /// </summary>
    /// <returns>Команда <c>checklist</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("checklist", "Чек-лист задачи.");
        cmd.Subcommands.Add(ChecklistGetCommand.Build());
        cmd.Subcommands.Add(ChecklistAddItemCommand.Build());
        cmd.Subcommands.Add(ChecklistToggleCommand.Build());
        cmd.Subcommands.Add(ChecklistUpdateCommand.Build());
        cmd.Subcommands.Add(ChecklistRemoveCommand.Build());
        return cmd;
    }
}
