namespace YandexTrackerCLI.Commands.Version;

using System.CommandLine;
using System.Text.Json;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt version delete &lt;id&gt;</c>: выполняет
/// <c>DELETE /v3/versions/{id}</c>.
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
///     Если сервер вернул непустой JSON (<c>200 OK</c> с телом), этот JSON выводится
///     как есть через <see cref="JsonWriter.Write"/>.
///   </description></item>
/// </list>
/// </remarks>
public static class VersionDeleteCommand
{
    /// <summary>
    /// Строит subcommand <c>delete</c> для <c>yt version</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var idArg = new Argument<string>("id") { Description = "Идентификатор версии." };
        var cmd = new Command("delete", "Удалить версию (DELETE /v3/versions/{id}).");
        cmd.Arguments.Add(idArg);
        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
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
                    $"versions/{Uri.EscapeDataString(id)}",
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
