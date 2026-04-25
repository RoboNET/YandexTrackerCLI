namespace YandexTrackerCLI.Commands.Component;

using System.CommandLine;

/// <summary>
/// Контейнер группы команд <c>yt component</c>. Регистрирует подкоманды работы
/// с компонентами очередей (list/get/create/update/delete).
/// </summary>
public static class ComponentCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>component</c> со всеми подкомандами.
    /// </summary>
    /// <returns>Команда <c>component</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("component", "Работа с компонентами очередей (queues/{queue}/components, components/{id}).");
        cmd.Subcommands.Add(ComponentListCommand.Build());
        cmd.Subcommands.Add(ComponentGetCommand.Build());
        cmd.Subcommands.Add(ComponentCreateCommand.Build());
        cmd.Subcommands.Add(ComponentUpdateCommand.Build());
        cmd.Subcommands.Add(ComponentDeleteCommand.Build());
        return cmd;
    }
}
