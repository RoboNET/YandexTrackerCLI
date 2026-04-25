namespace YandexTrackerCLI.Commands.Version;

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt version update &lt;id&gt;</c>: обновляет версию через
/// <c>PATCH /v3/versions/{id}</c>. Поддерживает typed-режим (все поля опциональны —
/// <c>--name</c>, <c>--description</c>, <c>--start-date</c>, <c>--due-date</c>, <c>--released</c>)
/// и raw-режим (<c>--json-file</c>/<c>--json-stdin</c>). Хотя бы один источник данных
/// обязателен. Даты валидируются как ISO 8601.
/// </summary>
public static class VersionUpdateCommand
{
    /// <summary>
    /// Строит subcommand <c>update</c> для <c>yt version</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var idArg = new Argument<string>("id") { Description = "Идентификатор версии." };
        var nameOpt = new Option<string?>("--name")
        {
            Description = "Новое название версии (typed-режим, опционально).",
        };
        var descriptionOpt = new Option<string?>("--description")
        {
            Description = "Новое описание версии (typed-режим, опционально).",
        };
        var startDateOpt = new Option<string?>("--start-date")
        {
            Description = "Новая дата начала в формате ISO 8601 (typed-режим, опционально).",
        };
        var dueDateOpt = new Option<string?>("--due-date")
        {
            Description = "Новая дата завершения в формате ISO 8601 (typed-режим, опционально).",
        };
        var releasedOpt = new Option<bool?>("--released")
        {
            Description = "Флаг \"версия выпущена\" (typed-режим, опционально).",
        };
        var jsonFileOpt = new Option<string?>("--json-file")
        {
            Description = "Путь к JSON-файлу с телом запроса (raw-режим).",
        };
        var jsonStdinOpt = new Option<bool>("--json-stdin")
        {
            Description = "Читать JSON-тело из stdin (raw-режим, альтернатива --json-file).",
        };

        var cmd = new Command("update", "Обновить версию (PATCH /v3/versions/{id}).");
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(nameOpt);
        cmd.Options.Add(descriptionOpt);
        cmd.Options.Add(startDateOpt);
        cmd.Options.Add(dueDateOpt);
        cmd.Options.Add(releasedOpt);
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var id = pr.GetValue(idArg)!;
                var name = pr.GetValue(nameOpt);
                var description = pr.GetValue(descriptionOpt);
                var startDate = pr.GetValue(startDateOpt);
                var dueDate = pr.GetValue(dueDateOpt);
                var released = pr.GetValue(releasedOpt);
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                var hasTyped =
                    !string.IsNullOrWhiteSpace(name)
                    || !string.IsNullOrWhiteSpace(description)
                    || !string.IsNullOrWhiteSpace(startDate)
                    || !string.IsNullOrWhiteSpace(dueDate)
                    || released.HasValue;
                var hasRaw = !string.IsNullOrWhiteSpace(jsonFile) || jsonStdin;

                if (hasTyped && hasRaw)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "version update: typed flags and --json-file/--json-stdin are mutually exclusive.");
                }

                string body;
                if (hasRaw)
                {
                    body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                        ?? throw new TrackerException(
                            ErrorCode.InvalidArgs,
                            "version update: --json-file/--json-stdin produced no content.");
                }
                else if (hasTyped)
                {
                    body = BuildTypedBody(name, description, startDate, dueDate, released);
                }
                else
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "version update: nothing to update (provide typed flags or --json-file/--json-stdin).");
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
                    $"versions/{Uri.EscapeDataString(id)}",
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
    /// которые фактически заданы. Даты предварительно валидируются как ISO 8601.
    /// </summary>
    /// <param name="name">Новое название (опционально).</param>
    /// <param name="description">Новое описание (опционально).</param>
    /// <param name="startDate">Новая дата начала, ISO 8601 (опционально).</param>
    /// <param name="dueDate">Новая дата завершения, ISO 8601 (опционально).</param>
    /// <param name="released">Флаг "выпущена" (опционально).</param>
    /// <returns>Сериализованное JSON-тело.</returns>
    /// <exception cref="TrackerException">
    /// Бросается с <see cref="ErrorCode.InvalidArgs"/>, если даты не парсятся как ISO 8601.
    /// </exception>
    private static string BuildTypedBody(
        string? name,
        string? description,
        string? startDate,
        string? dueDate,
        bool? released)
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

            if (!string.IsNullOrWhiteSpace(startDate))
            {
                VersionDateValidator.ValidateIsoDate(startDate, "--start-date");
                w.WriteString("startDate", startDate);
            }

            if (!string.IsNullOrWhiteSpace(dueDate))
            {
                VersionDateValidator.ValidateIsoDate(dueDate, "--due-date");
                w.WriteString("dueDate", dueDate);
            }

            if (released.HasValue)
            {
                w.WriteBoolean("released", released.Value);
            }

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
