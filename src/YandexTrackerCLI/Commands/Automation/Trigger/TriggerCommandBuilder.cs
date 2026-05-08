namespace YandexTrackerCLI.Commands.Automation.Trigger;

using System.CommandLine;

/// <summary>
/// Контейнер группы <c>yt automation trigger</c>. Регистрирует подкоманды
/// работы с триггерами очереди (CRUD и activate/deactivate).
/// </summary>
public static class TriggerCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>trigger</c> со всеми зарегистрированными подкомандами.
    /// </summary>
    /// <returns>Команда <c>trigger</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("trigger", "Триггеры очереди (CRUD + activate/deactivate).");
        cmd.Subcommands.Add(TriggerListCommand.Build());
        cmd.Subcommands.Add(TriggerGetCommand.Build());
        cmd.Subcommands.Add(TriggerCreateCommand.Build());
        cmd.Subcommands.Add(TriggerUpdateCommand.Build());
        cmd.Subcommands.Add(TriggerDeleteCommand.Build());
        return cmd;
    }
}
