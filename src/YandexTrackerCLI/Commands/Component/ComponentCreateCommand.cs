namespace YandexTrackerCLI.Commands.Component;

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt component create</c>: создаёт компонент через
/// <c>POST /v3/components</c>. Поддерживает два режима:
/// <list type="bullet">
///   <item><description>
///     <b>Typed</b> — требуются <c>--queue</c> и <c>--name</c>; опционально
///     <c>--description</c>, <c>--lead</c>, <c>--assign-auto</c>. Тело собирается
///     через <see cref="Utf8JsonWriter"/> с обёрткой полей <c>queue</c> и <c>lead</c>
///     в объекты (<c>{"key":...}</c> и <c>{"login":...}</c>).
///   </description></item>
///   <item><description>
///     <b>Raw</b> — <c>--json-file</c>/<c>--json-stdin</c>, тело уходит без изменений.
///   </description></item>
/// </list>
/// Режимы взаимоисключающи: одновременное указание typed-флага и raw-источника
/// приводит к ошибке <see cref="ErrorCode.InvalidArgs"/>.
/// </summary>
public static class ComponentCreateCommand
{
    /// <summary>
    /// Строит subcommand <c>create</c> для <c>yt component</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var queueOpt = new Option<string?>("--queue")
        {
            Description = "Ключ очереди (typed-режим, обязателен с --name).",
        };
        var nameOpt = new Option<string?>("--name")
        {
            Description = "Название компонента (typed-режим, обязателен с --queue).",
        };
        var descriptionOpt = new Option<string?>("--description")
        {
            Description = "Описание компонента (typed-режим, опционально).",
        };
        var leadOpt = new Option<string?>("--lead")
        {
            Description = "Логин владельца компонента (typed-режим, опционально).",
        };
        var assignAutoOpt = new Option<bool>("--assign-auto")
        {
            Description = "Автоматически назначать задачи владельцу (typed-режим, опционально).",
        };
        var jsonFileOpt = new Option<string?>("--json-file")
        {
            Description = "Путь к JSON-файлу с телом запроса (raw-режим).",
        };
        var jsonStdinOpt = new Option<bool>("--json-stdin")
        {
            Description = "Читать JSON-тело из stdin (raw-режим, альтернатива --json-file).",
        };

        var cmd = new Command("create", "Создать компонент (POST /v3/components).");
        cmd.Options.Add(queueOpt);
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
                var queue = pr.GetValue(queueOpt);
                var name = pr.GetValue(nameOpt);
                var description = pr.GetValue(descriptionOpt);
                var lead = pr.GetValue(leadOpt);
                var assignAuto = pr.GetValue(assignAutoOpt);
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                var hasTyped =
                    !string.IsNullOrWhiteSpace(queue)
                    || !string.IsNullOrWhiteSpace(name)
                    || !string.IsNullOrWhiteSpace(description)
                    || !string.IsNullOrWhiteSpace(lead)
                    || assignAuto;
                var hasRaw = !string.IsNullOrWhiteSpace(jsonFile) || jsonStdin;

                if (hasTyped && hasRaw)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "component create: typed flags and --json-file/--json-stdin are mutually exclusive.");
                }

                string body;
                if (hasRaw)
                {
                    body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                        ?? throw new TrackerException(
                            ErrorCode.InvalidArgs,
                            "component create: --json-file/--json-stdin produced no content.");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(queue) || string.IsNullOrWhiteSpace(name))
                    {
                        throw new TrackerException(
                            ErrorCode.InvalidArgs,
                            "component create: --queue and --name are required in typed mode (or use --json-file/--json-stdin).");
                    }

                    body = BuildTypedBody(queue, name, description, lead, assignAuto);
                }

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.PostJsonRawAsync("components", body, ct);
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
    /// Формирует JSON-тело запроса из typed-флагов. <c>queue</c> и <c>lead</c>
    /// оборачиваются в объекты (<c>{"key":...}</c> и <c>{"login":...}</c> соответственно).
    /// </summary>
    /// <param name="queue">Ключ очереди.</param>
    /// <param name="name">Название компонента.</param>
    /// <param name="description">Описание (опционально).</param>
    /// <param name="lead">Логин владельца (опционально).</param>
    /// <param name="assignAuto">Флаг автоназначения (только если <c>true</c>).</param>
    /// <returns>Сериализованное JSON-тело.</returns>
    private static string BuildTypedBody(
        string queue,
        string name,
        string? description,
        string? lead,
        bool assignAuto)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteStartObject("queue");
            w.WriteString("key", queue);
            w.WriteEndObject();
            w.WriteString("name", name);
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
