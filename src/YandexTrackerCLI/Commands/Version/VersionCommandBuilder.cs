namespace YandexTrackerCLI.Commands.Version;

using System.CommandLine;

/// <summary>
/// Контейнер группы команд <c>yt version</c>. Регистрирует подкоманды работы
/// с версиями очередей (list/get/create/update/delete).
/// </summary>
public static class VersionCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>version</c> со всеми подкомандами.
    /// </summary>
    /// <returns>Команда <c>version</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("version", "Работа с версиями очередей (queues/{queue}/versions, versions/{id}).");
        cmd.Subcommands.Add(VersionListCommand.Build());
        cmd.Subcommands.Add(VersionGetCommand.Build());
        cmd.Subcommands.Add(VersionCreateCommand.Build());
        cmd.Subcommands.Add(VersionUpdateCommand.Build());
        cmd.Subcommands.Add(VersionDeleteCommand.Build());
        return cmd;
    }
}
