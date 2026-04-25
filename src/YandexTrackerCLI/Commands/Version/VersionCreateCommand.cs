namespace YandexTrackerCLI.Commands.Version;

using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt version create</c>: создаёт версию через <c>POST /v3/versions</c>.
/// Поддерживает два режима:
/// <list type="bullet">
///   <item><description>
///     <b>Typed</b> — требуются <c>--queue</c> и <c>--name</c>; опционально
///     <c>--description</c>, <c>--start-date</c>, <c>--due-date</c>, <c>--released</c>.
///     Тело собирается через <see cref="Utf8JsonWriter"/>, <c>queue</c> оборачивается
///     в объект <c>{"key":...}</c>. Даты валидируются как ISO 8601
///     (<see cref="DateTimeOffset.TryParse(string, IFormatProvider, DateTimeStyles, out DateTimeOffset)"/>).
///   </description></item>
///   <item><description>
///     <b>Raw</b> — <c>--json-file</c>/<c>--json-stdin</c>, тело уходит без изменений.
///   </description></item>
/// </list>
/// Режимы взаимоисключающи: одновременное указание typed-флага и raw-источника
/// приводит к ошибке <see cref="ErrorCode.InvalidArgs"/>.
/// </summary>
public static class VersionCreateCommand
{
    /// <summary>
    /// Строит subcommand <c>create</c> для <c>yt version</c>.
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
            Description = "Название версии (typed-режим, обязателен с --queue).",
        };
        var descriptionOpt = new Option<string?>("--description")
        {
            Description = "Описание версии (typed-режим, опционально).",
        };
        var startDateOpt = new Option<string?>("--start-date")
        {
            Description = "Дата начала в формате ISO 8601 (typed-режим, опционально).",
        };
        var dueDateOpt = new Option<string?>("--due-date")
        {
            Description = "Дата завершения в формате ISO 8601 (typed-режим, опционально).",
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

        var cmd = new Command("create", "Создать версию (POST /v3/versions).");
        cmd.Options.Add(queueOpt);
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
                var queue = pr.GetValue(queueOpt);
                var name = pr.GetValue(nameOpt);
                var description = pr.GetValue(descriptionOpt);
                var startDate = pr.GetValue(startDateOpt);
                var dueDate = pr.GetValue(dueDateOpt);
                var released = pr.GetValue(releasedOpt);
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                var hasTyped =
                    !string.IsNullOrWhiteSpace(queue)
                    || !string.IsNullOrWhiteSpace(name)
                    || !string.IsNullOrWhiteSpace(description)
                    || !string.IsNullOrWhiteSpace(startDate)
                    || !string.IsNullOrWhiteSpace(dueDate)
                    || released.HasValue;
                var hasRaw = !string.IsNullOrWhiteSpace(jsonFile) || jsonStdin;

                if (hasTyped && hasRaw)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "version create: typed flags and --json-file/--json-stdin are mutually exclusive.");
                }

                string body;
                if (hasRaw)
                {
                    body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                        ?? throw new TrackerException(
                            ErrorCode.InvalidArgs,
                            "version create: --json-file/--json-stdin produced no content.");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(queue) || string.IsNullOrWhiteSpace(name))
                    {
                        throw new TrackerException(
                            ErrorCode.InvalidArgs,
                            "version create: --queue and --name are required in typed mode (or use --json-file/--json-stdin).");
                    }

                    body = BuildTypedBody(queue, name, description, startDate, dueDate, released);
                }

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                var result = await ctx.Client.PostJsonRawAsync("versions", body, ct);
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
    /// Формирует JSON-тело запроса из typed-флагов. <c>queue</c> оборачивается в
    /// объект <c>{"key":...}</c>. Поля <c>startDate</c>/<c>dueDate</c> валидируются
    /// как ISO 8601 перед записью.
    /// </summary>
    /// <param name="queue">Ключ очереди.</param>
    /// <param name="name">Название версии.</param>
    /// <param name="description">Описание (опционально).</param>
    /// <param name="startDate">Дата начала, ISO 8601 (опционально).</param>
    /// <param name="dueDate">Дата завершения, ISO 8601 (опционально).</param>
    /// <param name="released">Флаг "выпущена" (опционально).</param>
    /// <returns>Сериализованное JSON-тело.</returns>
    /// <exception cref="TrackerException">
    /// Бросается с <see cref="ErrorCode.InvalidArgs"/>, если даты не парсятся как ISO 8601.
    /// </exception>
    private static string BuildTypedBody(
        string queue,
        string name,
        string? description,
        string? startDate,
        string? dueDate,
        bool? released)
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

/// <summary>
/// Внутренний хелпер валидации ISO 8601 дат для <c>yt version</c> команд.
/// Шарится между <see cref="VersionCreateCommand"/> и <see cref="VersionUpdateCommand"/>.
/// </summary>
internal static class VersionDateValidator
{
    /// <summary>
    /// Проверяет, что строка парсится как ISO 8601 дата/дата-время
    /// (<see cref="DateTimeOffset.TryParse(string, IFormatProvider, DateTimeStyles, out DateTimeOffset)"/>
    /// с <see cref="CultureInfo.InvariantCulture"/> и <see cref="DateTimeStyles.AssumeUniversal"/>).
    /// </summary>
    /// <param name="value">Значение опции.</param>
    /// <param name="optionName">Имя опции для сообщения об ошибке (например, <c>--start-date</c>).</param>
    /// <exception cref="TrackerException">
    /// Бросается с <see cref="ErrorCode.InvalidArgs"/>, если строка не парсится.
    /// </exception>
    internal static void ValidateIsoDate(string value, string optionName)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out _))
        {
            throw new TrackerException(
                ErrorCode.InvalidArgs,
                $"{optionName}: invalid ISO 8601 date '{value}'.");
        }
    }
}
