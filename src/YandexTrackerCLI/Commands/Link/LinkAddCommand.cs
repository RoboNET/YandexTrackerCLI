namespace YandexTrackerCLI.Commands.Link;

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt link add &lt;issue-key&gt; --to &lt;other-key&gt; --type &lt;relationship&gt;</c>:
/// добавляет связь задачи (<c>POST /v3/issues/{key}/links</c>). Поддерживает два режима:
/// <list type="bullet">
///   <item><description>
///     <b>Typed</b> — через обязательные <c>--to</c> и <c>--type</c>. В теле запроса
///     формируется объект <c>{"relationship":"&lt;type&gt;","issue":"&lt;other-key&gt;"}</c>.
///     Допустимые значения <c>--type</c> валидируются парсером через
///     <see cref="Option{T}.AcceptOnlyFromAmong(string[])"/>.
///   </description></item>
///   <item><description>
///     <b>Raw JSON</b> — через <c>--json-file &lt;path&gt;</c> или <c>--json-stdin</c>
///     (взаимоисключается с typed-опциями).
///   </description></item>
/// </list>
/// </summary>
public static class LinkAddCommand
{
    /// <summary>
    /// Строит subcommand <c>add</c> для <c>yt link</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("issue-key") { Description = "Ключ задачи (например DEV-1)." };
        var toOpt = new Option<string?>("--to") { Description = "Ключ связанной задачи (например DEV-2)." };
        var typeOpt = new Option<string?>("--type") { Description = "Тип связи." };
        typeOpt.AcceptOnlyFromAmong(
            "relates",
            "is-dependent-by",
            "depends-on",
            "is-subtask-of",
            "subtasks",
            "duplicates",
            "is-duplicated-by",
            "is-epic-of",
            "has-epic");
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу с телом запроса." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

        var cmd = new Command(
            "add",
            "Добавить связь к задаче (POST /v3/issues/{key}/links).");
        cmd.Arguments.Add(keyArg);
        cmd.Options.Add(toOpt);
        cmd.Options.Add(typeOpt);
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                var toKey = pr.GetValue(toOpt);
                var type = pr.GetValue(typeOpt);
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                var hasTo = !string.IsNullOrWhiteSpace(toKey);
                var hasType = !string.IsNullOrWhiteSpace(type);
                var hasTyped = hasTo || hasType;
                var hasRaw = !string.IsNullOrWhiteSpace(jsonFile) || jsonStdin;

                if (hasTyped && hasRaw)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "Cannot combine --to/--type with --json-file/--json-stdin.");
                }

                if (!hasTyped && !hasRaw)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "Provide --to and --type, or --json-file/--json-stdin.");
                }

                string body;
                if (hasRaw)
                {
                    body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                        ?? throw new TrackerException(ErrorCode.InvalidArgs, "Empty request body.");
                }
                else
                {
                    if (!hasTo || !hasType)
                    {
                        throw new TrackerException(
                            ErrorCode.InvalidArgs,
                            "Both --to and --type are required in typed mode.");
                    }

                    body = BuildTypedBody(type!, toKey!);
                }

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.PostJsonRawAsync(
                    $"issues/{Uri.EscapeDataString(key)}/links",
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

    /// <summary>
    /// Строит JSON-тело запроса для типизированного режима
    /// (<c>{"relationship":"&lt;type&gt;","issue":"&lt;other-key&gt;"}</c>).
    /// </summary>
    /// <param name="type">Тип связи (уже провалидирован парсером).</param>
    /// <param name="toKey">Ключ связанной задачи.</param>
    /// <returns>Сериализованный компактный JSON-объект.</returns>
    internal static string BuildTypedBody(string type, string toKey)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("relationship", type);
            w.WriteString("issue", toKey);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
