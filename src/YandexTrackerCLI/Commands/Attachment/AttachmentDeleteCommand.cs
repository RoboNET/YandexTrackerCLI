namespace YandexTrackerCLI.Commands.Attachment;

using System.CommandLine;
using System.Text.Json;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt attachment delete &lt;issue-key&gt; &lt;attachment-id&gt;</c>: выполняет
/// <c>DELETE /v3/issues/{key}/attachments/{id}</c>.
/// </summary>
/// <remarks>
/// Поведение вывода:
/// <list type="bullet">
///   <item><description>
///     Если сервер вернул <c>204 No Content</c> (тело отсутствует —
///     <see cref="JsonValueKind.Undefined"/>), печатается success-маркер вида
///     <c>{"deleted":"&lt;id&gt;"}</c> через <see cref="CommandOutput.WriteSingleField"/>.
///   </description></item>
///   <item><description>
///     Если сервер вернул непустой JSON, он выводится через <see cref="JsonWriter.Write"/>.
///   </description></item>
/// </list>
/// </remarks>
public static class AttachmentDeleteCommand
{
    /// <summary>
    /// Строит subcommand <c>delete</c> для <c>yt attachment</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("issue-key") { Description = "Ключ задачи (например DEV-1)." };
        var idArg = new Argument<string>("attachment-id") { Description = "Идентификатор вложения." };

        var cmd = new Command(
            "delete",
            "Удалить вложение (DELETE /v3/issues/{key}/attachments/{id}).");
        cmd.Arguments.Add(keyArg);
        cmd.Arguments.Add(idArg);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                var id = pr.GetValue(idArg)!;
                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.DeleteAsync(
                    $"issues/{Uri.EscapeDataString(key)}/attachments/{Uri.EscapeDataString(id)}",
                    ct);
                if (result.ValueKind == JsonValueKind.Undefined)
                {
                    CommandOutput.WriteSingleField("deleted", id);
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
