namespace YandexTrackerCLI.Commands.Automation.Trigger;

using System.CommandLine;

/// <summary>
/// Команда <c>yt automation trigger deactivate &lt;id&gt; --queue &lt;key&gt;</c>:
/// выполняет <c>PATCH /v3/queues/{queue}/triggers/{id}</c> с фиксированным
/// телом <c>{"active":false}</c>. Делегирует сборку команду-фабрике
/// <see cref="TriggerActivateCommand.BuildSetActive"/>.
/// </summary>
public static class TriggerDeactivateCommand
{
    /// <summary>
    /// Строит subcommand <c>deactivate</c> для группы <c>yt automation trigger</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build() => TriggerActivateCommand.BuildSetActive(false,
        "deactivate", "Деактивировать триггер (PATCH active=false).");
}
