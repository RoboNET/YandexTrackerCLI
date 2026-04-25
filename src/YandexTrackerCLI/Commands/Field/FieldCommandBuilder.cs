namespace YandexTrackerCLI.Commands.Field;

using System.CommandLine;

/// <summary>
/// Контейнер группы команд <c>yt field</c>. Регистрирует подкоманды работы
/// с полями задач (list/get) — глобальными (<c>fields</c>) или локальными
/// для очереди (<c>queues/{key}/localFields</c>).
/// </summary>
public static class FieldCommandBuilder
{
    /// <summary>
    /// Собирает subtree <c>field</c> со всеми подкомандами.
    /// </summary>
    /// <returns>Команда <c>field</c> с прикреплёнными подкомандами.</returns>
    public static Command Build()
    {
        var cmd = new Command("field", "Поля задач: список и получение (global или local для очереди).");
        cmd.Subcommands.Add(FieldListCommand.Build());
        cmd.Subcommands.Add(FieldGetCommand.Build());
        return cmd;
    }
}
