namespace YandexTrackerCLI.Commands.Project;

using System.CommandLine;

/// <summary>
/// Контейнер группы команд <c>yt project</c>. Регистрирует подкоманды работы
/// с проектами (list/get/create/update/delete).
/// </summary>
public static class ProjectCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>project</c> со всеми подкомандами.
    /// </summary>
    /// <returns>Команда <c>project</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("project", "Работа с проектами (entities/project).");
        cmd.Subcommands.Add(ProjectListCommand.Build());
        cmd.Subcommands.Add(ProjectGetCommand.Build());
        cmd.Subcommands.Add(ProjectCreateCommand.Build());
        cmd.Subcommands.Add(ProjectUpdateCommand.Build());
        cmd.Subcommands.Add(ProjectDeleteCommand.Build());
        return cmd;
    }
}
