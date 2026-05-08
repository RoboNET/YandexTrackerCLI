namespace YandexTrackerCLI.Commands.Automation.Autoaction;

using System.CommandLine;

/// <summary>
/// Команда <c>yt automation autoaction deactivate &lt;id&gt; --queue &lt;key&gt;</c>:
/// выполняет <c>PATCH /v3/queues/{queue}/autoactions/{id}</c> с фиксированным
/// телом <c>{"active":false}</c>. Делегирует сборку команду-фабрике
/// <see cref="AutoactionActivateCommand.BuildSetActive"/>.
/// </summary>
public static class AutoactionDeactivateCommand
{
    /// <summary>
    /// Строит subcommand <c>deactivate</c> для группы <c>yt automation autoaction</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build() => AutoactionActivateCommand.BuildSetActive(false,
        "deactivate", "Деактивировать автодействие (PATCH active=false).");
}
