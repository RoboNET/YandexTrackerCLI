namespace YandexTrackerCLI.Commands.Component;

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt component update &lt;id&gt;</c>: обновляет компонент через
/// <c>PATCH /v3/components/{id}</c>. Поддерживает typed-режим (все поля опциональны —
/// <c>--name</c>, <c>--description</c>, <c>--lead</c>, <c>--assign-auto</c>) и raw-режим
/// (<c>--json-file</c>/<c>--json-stdin</c>). Хотя бы один источник данных обязателен.
/// </summary>
public static class ComponentUpdateCommand
{
    /// <summary>
    /// Строит subcommand <c>update</c> для <c>yt component</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var idArg = new Argument<string>("id") { Description = "Идентификатор компонента." };
        var nameOpt = new Option<string?>("--name")
        {
            Description = "Новое название компонента (typed-режим, опционально).",
        };
        var descriptionOpt = new Option<string?>("--description")
        {
            Description = "Новое описание компонента (typed-режим, опционально).",
        };
        var leadOpt = new Option<string?>("--lead")
        {
            Description = "Логин нового владельца (typed-режим, опционально).",
        };
        var assignAutoOpt = new Option<bool>("--assign-auto")
        {
            Description = "Установить флаг автоназначения (typed-режим, опционально).",
        };
        var jsonFileOpt = new Option<string?>("--json-file")
        {
            Description = "Путь к JSON-файлу с телом запроса (raw-режим).",
        };
        var jsonStdinOpt = new Option<bool>("--json-stdin")
        {
            Description = "Читать JSON-тело из stdin (raw-режим, альтернатива --json-file).",
        };

        var cmd = new Command("update", "Обновить компонент (PATCH /v3/components/{id}).");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(nameOpt);
        cmd.Options.Add(descriptionOpt);
        cmd.Options.Add(leadOpt);
        cmd.Options.Add(assignAutoOpt);
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var id = pr.GetValue(idArg)!;
                var name = pr.GetValue(nameOpt);
                var description = pr.GetValue(descriptionOpt);
                var lead = pr.GetValue(leadOpt);
                var assignAuto = pr.GetValue(assignAutoOpt);
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                var hasTyped =
                    !string.IsNullOrWhiteSpace(name)
                    || !string.IsNullOrWhiteSpace(description)
                    || !string.IsNullOrWhiteSpace(lead)
                    || assignAuto;
                var hasRaw = !string.IsNullOrWhiteSpace(jsonFile) || jsonStdin;

                if (hasTyped && hasRaw)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "component update: typed flags and --json-file/--json-stdin are mutually exclusive.");
                }

                string body;
                if (hasRaw)
                {
                    body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                        ?? throw new TrackerException(
                            ErrorCode.InvalidArgs,
                            "component update: --json-file/--json-stdin produced no content.");
                }
                else if (hasTyped)
                {
                    body = BuildTypedBody(name, description, lead, assignAuto);
                }
                else
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "component update: nothing to update (provide typed flags or --json-file/--json-stdin).");
                }

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.PatchJsonAsync(
                    $"components/{Uri.EscapeDataString(id)}",
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
    /// Формирует JSON-тело PATCH-запроса из typed-флагов. Пишет только те поля,
    /// которые фактически заданы. <c>lead</c> оборачивается в объект <c>{"login":...}</c>.
    /// </summary>
    /// <param name="name">Новое название (опционально).</param>
    /// <param name="description">Новое описание (опционально).</param>
    /// <param name="lead">Логин нового владельца (опционально).</param>
    /// <param name="assignAuto">Флаг автоназначения (пишется только если <c>true</c>).</param>
    /// <returns>Сериализованное JSON-тело.</returns>
    private static string BuildTypedBody(
        string? name,
        string? description,
        string? lead,
        bool assignAuto)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            if (!string.IsNullOrWhiteSpace(name))
            {
                w.WriteString("name", name);
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                w.WriteString("description", description);
            }

            if (!string.IsNullOrWhiteSpace(lead))
            {
                w.WriteStartObject("lead");
                w.WriteString("login", lead);
                w.WriteEndObject();
            }

            if (assignAuto)
            {
                w.WriteBoolean("assignAuto", true);
            }

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
