namespace YandexTrackerCLI.Commands.Comment;

using System.CommandLine;

/// <summary>
/// Контейнер группы команд <c>yt comment</c>: собирает subtree с подкомандами
/// <c>list</c>, <c>add</c>, <c>update</c>, <c>delete</c>.
/// </summary>
public static class CommentCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>comment</c> со всеми подкомандами.
    /// </summary>
    /// <returns>Команда <c>comment</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("comment", "Работа с комментариями задач.");
        cmd.Subcommands.Add(CommentListCommand.Build());
        cmd.Subcommands.Add(CommentAddCommand.Build());
        cmd.Subcommands.Add(CommentUpdateCommand.Build());
        cmd.Subcommands.Add(CommentDeleteCommand.Build());
        return cmd;
    }
}
