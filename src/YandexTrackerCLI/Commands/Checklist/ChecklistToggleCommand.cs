namespace YandexTrackerCLI.Commands.Checklist;

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt checklist toggle &lt;issue-key&gt; &lt;item-id&gt;</c>: переключает состояние
/// <c>checked</c> пункта чек-листа через <c>PATCH /v3/issues/{key}/checklistItems/{itemId}</c>.
/// </summary>
/// <remarks>
/// Поведение:
/// <list type="bullet">
///   <item><description>
///     Если передан <c>--checked true|false</c> — выполняется один PATCH с
///     <c>{"checked":&lt;value&gt;}</c>.
///   </description></item>
///   <item><description>
///     Если <c>--checked</c> не передан — сначала GET
///     <c>/v3/issues/{key}/checklistItems</c>, затем ищется элемент с заданным
///     <c>item-id</c>, значение <c>checked</c> инвертируется и выполняется PATCH.
///     Если пункт не найден — <see cref="ErrorCode.NotFound"/> (exit 5).
///   </description></item>
/// </list>
/// </remarks>
public static class ChecklistToggleCommand
{
    /// <summary>
    /// Строит subcommand <c>toggle</c> для <c>yt checklist</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("issue-key") { Description = "Ключ задачи (например DEV-1)." };
        var itemIdArg = new Argument<string>("item-id") { Description = "Идентификатор пункта чек-листа." };
        var checkedOpt = new Option<bool?>("--checked")
        {
            Description = "Установить состояние явно (true|false). Если опущено — состояние инвертируется.",
        };

        var cmd = new Command(
            "toggle",
            "Переключить состояние пункта чек-листа (PATCH /v3/issues/{key}/checklistItems/{itemId}).");
        cmd.Arguments.Add(keyArg);
        cmd.Arguments.Add(itemIdArg);
        cmd.Options.Add(checkedOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                var itemId = pr.GetValue(itemIdArg)!;
                var explicitChecked = pr.GetValue(checkedOpt);

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                bool target;
                if (explicitChecked.HasValue)
                {
                    target = explicitChecked.Value;
                }
                else
                {
                    var list = await ctx.Client.GetAsync(
                        $"issues/{Uri.EscapeDataString(key)}/checklistItems",
                        ct);

                    if (list.ValueKind != JsonValueKind.Array)
                    {
                        throw new TrackerException(
                            ErrorCode.Unexpected,
                            "Expected JSON array in checklist response.");
                    }

                    JsonElement? found = null;
                    foreach (var el in list.EnumerateArray())
                    {
                        if (el.TryGetProperty("id", out var idEl)
                            && idEl.ValueKind == JsonValueKind.String
                            && idEl.GetString() == itemId)
                        {
                            found = el;
                            break;
                        }
                    }
                    if (found is null)
                    {
                        throw new TrackerException(
                            ErrorCode.NotFound,
                            $"Checklist item '{itemId}' not found on issue '{key}'.");
                    }
                    var current = found.Value.TryGetProperty("checked", out var c)
                        && c.ValueKind == JsonValueKind.True;
                    target = !current;
                }

                string body;
                using (var ms = new MemoryStream())
                {
                    using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
                    {
                        w.WriteStartObject();
                        w.WriteBoolean("checked", target);
                        w.WriteEndObject();
                    }
                    body = Encoding.UTF8.GetString(ms.ToArray());
                }

                var result = await ctx.Client.PatchJsonAsync(
                    $"issues/{Uri.EscapeDataString(key)}/checklistItems/{Uri.EscapeDataString(itemId)}",
                    body,
                    ct);
                JsonWriter.Write(Console.Out, result, ctx.EffectiveOutputFormat, pretty: !Console.IsOutputRedirected);
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
