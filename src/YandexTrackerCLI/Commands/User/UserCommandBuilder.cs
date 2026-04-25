namespace YandexTrackerCLI.Commands.User;

using System.CommandLine;

/// <summary>
/// Контейнер группы команд <c>yt user</c>. Регистрирует подкоманды работы
/// с пользователями (me/get/search/list).
/// </summary>
public static class UserCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>user</c> со всеми подкомандами.
    /// </summary>
    /// <returns>Команда <c>user</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("user", "Пользователи.");
        cmd.Subcommands.Add(UserMeCommand.Build());
        cmd.Subcommands.Add(UserGetCommand.Build());
        cmd.Subcommands.Add(UserSearchCommand.Build());
        cmd.Subcommands.Add(UserListCommand.Build());
        return cmd;
    }
}
