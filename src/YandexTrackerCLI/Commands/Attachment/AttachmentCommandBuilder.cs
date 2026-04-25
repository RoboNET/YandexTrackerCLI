namespace YandexTrackerCLI.Commands.Attachment;

using System.CommandLine;

/// <summary>
/// Контейнер группы команд <c>yt attachment</c>: собирает subtree с подкомандами
/// работы с вложениями задач (list/upload/download/delete).
/// </summary>
public static class AttachmentCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>attachment</c> со всеми подкомандами.
    /// </summary>
    /// <returns>Команда <c>attachment</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("attachment", "Вложения задач.");
        cmd.Subcommands.Add(AttachmentListCommand.Build());
        cmd.Subcommands.Add(AttachmentUploadCommand.Build());
        cmd.Subcommands.Add(AttachmentDownloadCommand.Build());
        cmd.Subcommands.Add(AttachmentDeleteCommand.Build());
        return cmd;
    }
}
