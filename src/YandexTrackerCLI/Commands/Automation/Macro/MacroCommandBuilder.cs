namespace YandexTrackerCLI.Commands.Automation.Macro;

using System.CommandLine;

/// <summary>
/// Контейнер группы <c>yt automation macro</c>. Регистрирует подкоманды
/// работы с макросами очереди (CRUD без activate/deactivate).
/// </summary>
public static class MacroCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>macro</c> со всеми зарегистрированными подкомандами.
    /// </summary>
    /// <returns>Команда <c>macro</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("macro", "Макросы очереди (CRUD).");
        cmd.Subcommands.Add(MacroListCommand.Build());
        cmd.Subcommands.Add(MacroGetCommand.Build());
        cmd.Subcommands.Add(MacroCreateCommand.Build());
        cmd.Subcommands.Add(MacroUpdateCommand.Build());
        cmd.Subcommands.Add(MacroDeleteCommand.Build());
        return cmd;
    }
}
