namespace YandexTrackerCLI.Commands.Link;

using System.CommandLine;
using System.Text.Json;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt link remove &lt;issue-key&gt; &lt;link-id&gt;</c>: выполняет
/// <c>DELETE /v3/issues/{key}/links/{link-id}</c>.
/// </summary>
/// <remarks>
/// Поведение вывода:
/// <list type="bullet">
///   <item><description>
///     Если сервер вернул <c>204 No Content</c> (тело отсутствует —
///     <see cref="JsonValueKind.Undefined"/>), печатается success-маркер вида
///     <c>{"removed":"&lt;linkId&gt;"}</c> через <see cref="CommandOutput.WriteSingleField"/>.
///   </description></item>
///   <item><description>
///     Если сервер вернул непустой JSON, он выводится через <see cref="JsonWriter.Write"/>.
///   </description></item>
/// </list>
/// </remarks>
public static class LinkRemoveCommand
{
    /// <summary>
    /// Строит subcommand <c>remove</c> для <c>yt link</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("issue-key") { Description = "Ключ задачи (например DEV-1)." };
        var linkIdArg = new Argument<string>("link-id") { Description = "Идентификатор связи." };

        var cmd = new Command(
            "remove",
            "Удалить связь задачи (DELETE /v3/issues/{key}/links/{link-id}).");
        cmd.Arguments.Add(keyArg);
        cmd.Arguments.Add(linkIdArg);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                var linkId = pr.GetValue(linkIdArg)!;
                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.DeleteAsync(
                    $"issues/{Uri.EscapeDataString(key)}/links/{Uri.EscapeDataString(linkId)}",
                    ct);
                if (result.ValueKind == JsonValueKind.Undefined)
                {
                    CommandOutput.WriteSingleField("removed", linkId);
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
