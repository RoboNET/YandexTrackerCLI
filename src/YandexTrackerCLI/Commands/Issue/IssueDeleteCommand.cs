namespace YandexTrackerCLI.Commands.Issue;

using System.CommandLine;
using System.Text.Json;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt issue delete &lt;key&gt;</c>: выполняет <c>DELETE /v3/issues/{key}</c>.
/// </summary>
/// <remarks>
/// Поведение вывода:
/// <list type="bullet">
///   <item><description>
///     Если сервер вернул <c>204 No Content</c> (тело отсутствует —
///     <see cref="JsonValueKind.Undefined"/>), печатается success-маркер вида
///     <c>{"deleted":"&lt;key&gt;"}</c> через <see cref="CommandOutput.WriteSingleField"/>.
///   </description></item>
///   <item><description>
///     Если сервер вернул непустой JSON (<c>200 OK</c> с телом), этот JSON выводится как есть
///     через <see cref="JsonWriter.Write"/>.
///   </description></item>
/// </list>
/// </remarks>
public static class IssueDeleteCommand
{
    /// <summary>
    /// Строит subcommand <c>delete</c> для <c>yt issue</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("key") { Description = "Ключ задачи." };
        var cmd = new Command("delete", "Удалить задачу (DELETE /v3/issues/{key}).");
        cmd.Arguments.Add(keyArg);
        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.DeleteAsync($"issues/{Uri.EscapeDataString(key)}", ct);
                if (result.ValueKind == JsonValueKind.Undefined)
                {
                    CommandOutput.WriteSingleField("deleted", key);
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
