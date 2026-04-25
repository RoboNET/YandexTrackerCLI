namespace YandexTrackerCLI.Commands.Checklist;

using System.CommandLine;
using System.Text.Json;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt checklist remove &lt;issue-key&gt; &lt;item-id&gt;</c>: выполняет
/// <c>DELETE /v3/issues/{key}/checklistItems/{itemId}</c>.
/// </summary>
/// <remarks>
/// Поведение вывода:
/// <list type="bullet">
///   <item><description>
///     Если сервер вернул <c>204 No Content</c> (тело отсутствует —
///     <see cref="JsonValueKind.Undefined"/>), печатается success-маркер вида
///     <c>{"removed":"&lt;itemId&gt;"}</c> через <see cref="CommandOutput.WriteSingleField"/>.
///   </description></item>
///   <item><description>
///     Если сервер вернул непустой JSON, он выводится через <see cref="JsonWriter.Write"/>.
///   </description></item>
/// </list>
/// </remarks>
public static class ChecklistRemoveCommand
{
    /// <summary>
    /// Строит subcommand <c>remove</c> для <c>yt checklist</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("issue-key") { Description = "Ключ задачи (например DEV-1)." };
        var itemIdArg = new Argument<string>("item-id") { Description = "Идентификатор пункта чек-листа." };

        var cmd = new Command(
            "remove",
            "Удалить пункт чек-листа (DELETE /v3/issues/{key}/checklistItems/{itemId}).");
        cmd.Arguments.Add(keyArg);
        cmd.Arguments.Add(itemIdArg);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                var itemId = pr.GetValue(itemIdArg)!;
                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.DeleteAsync(
                    $"issues/{Uri.EscapeDataString(key)}/checklistItems/{Uri.EscapeDataString(itemId)}",
                    ct);
                if (result.ValueKind == JsonValueKind.Undefined)
                {
                    CommandOutput.WriteSingleField("removed", itemId);
                }
                else
                {
                    JsonWriter.Write(Console.Out, result, ctx.EffectiveOutputFormat, pretty: !Console.IsOutputRedirected);
                }

                return 0;
            }
            catch (TrackerException ex)
            {
                ErrorWriter.Write(Console.Error, ex);
                return ex.Code.ToExitCode();
            }
        });
        return cmd;
    }
}
