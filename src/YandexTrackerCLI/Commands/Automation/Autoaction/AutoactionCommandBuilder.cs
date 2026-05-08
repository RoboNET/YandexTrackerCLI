namespace YandexTrackerCLI.Commands.Automation.Autoaction;

using System.CommandLine;

/// <summary>
/// Контейнер группы <c>yt automation autoaction</c>. Регистрирует подкоманды
/// работы с автодействиями очереди (CRUD и activate/deactivate).
/// </summary>
public static class AutoactionCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>autoaction</c> со всеми зарегистрированными подкомандами.
    /// </summary>
    /// <returns>Команда <c>autoaction</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("autoaction", "Автодействия очереди (CRUD + activate/deactivate).");
        cmd.Subcommands.Add(AutoactionListCommand.Build());
        cmd.Subcommands.Add(AutoactionGetCommand.Build());
        cmd.Subcommands.Add(AutoactionCreateCommand.Build());
        cmd.Subcommands.Add(AutoactionUpdateCommand.Build());
        cmd.Subcommands.Add(AutoactionDeleteCommand.Build());
        cmd.Subcommands.Add(AutoactionActivateCommand.Build());
        cmd.Subcommands.Add(AutoactionDeactivateCommand.Build());
        return cmd;
    }
}
