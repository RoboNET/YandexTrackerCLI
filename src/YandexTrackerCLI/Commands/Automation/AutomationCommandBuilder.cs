namespace YandexTrackerCLI.Commands.Automation;

using System.CommandLine;
using Autoaction;
using Macro;
using Trigger;

/// <summary>
/// Контейнер группы <c>yt automation</c> — автоматизации очереди:
/// триггеры, автодействия, макросы.
/// </summary>
public static class AutomationCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>automation</c> со всеми зарегистрированными группами подкоманд.
    /// </summary>
    /// <returns>Команда <c>automation</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("automation", "Автоматизации очереди: триггеры, автодействия, макросы.");
        cmd.Subcommands.Add(TriggerCommandBuilder.Build());
        cmd.Subcommands.Add(AutoactionCommandBuilder.Build());
        cmd.Subcommands.Add(MacroCommandBuilder.Build());
        return cmd;
    }
}
