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
/// Тело собирается через <see cref="JsonBodyReader.ReadAndMerge"/>: scalar-флаги
/// (<c>--name</c>, <c>--description</c>, <c>--start-date</c>, <c>--due-date</c>,
/// <c>--released</c>) мерджатся поверх raw-payload. Nested-флаг <c>--queue</c>
/// синтезирует базовый объект <c>{"queue":{"key":...}}</c> и не сочетается
/// с <c>--json-file</c>/<c>--json-stdin</c>.
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
            Description = "Ключ очереди (синтезирует {\"queue\":{\"key\":...}}; не сочетается с --json-*).",
        };
        var nameOpt = new Option<string?>("--name") { Description = "Название версии (override поля name)." };
        var descriptionOpt = new Option<string?>("--description") { Description = "Описание (override поля description)." };
        var startDateOpt = new Option<string?>("--start-date") { Description = "Дата начала ISO 8601 (override поля startDate)." };
        var dueDateOpt = new Option<string?>("--due-date") { Description = "Дата завершения ISO 8601 (override поля dueDate)." };
        var releasedOpt = new Option<bool?>("--released") { Description = "Флаг released (override поля released)." };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу с телом запроса." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

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

                var hasNestedTyped = !string.IsNullOrWhiteSpace(queue);
                var hasRawSource = !string.IsNullOrWhiteSpace(jsonFile) || jsonStdin;

                if (hasNestedTyped && hasRawSource)
                {
                    throw new TrackerException(ErrorCode.InvalidArgs,
                        "version create: --queue cannot be combined with --json-file/--json-stdin.");
                }

                if (!string.IsNullOrWhiteSpace(startDate))
                {
                    VersionDateValidator.ValidateIsoDate(startDate!, "--start-date");
                }
                if (!string.IsNullOrWhiteSpace(dueDate))
                {
                    VersionDateValidator.ValidateIsoDate(dueDate!, "--due-date");
                }

                var overrides = new List<(string, JsonBodyMerger.OverrideValue)>();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    overrides.Add(("name", JsonBodyMerger.OverrideValue.Of(name!)));
                }
                if (!string.IsNullOrWhiteSpace(description))
                {
                    overrides.Add(("description", JsonBodyMerger.OverrideValue.Of(description!)));
                }
                if (!string.IsNullOrWhiteSpace(startDate))
                {
                    overrides.Add(("startDate", JsonBodyMerger.OverrideValue.Of(startDate!)));
                }
                if (!string.IsNullOrWhiteSpace(dueDate))
                {
                    overrides.Add(("dueDate", JsonBodyMerger.OverrideValue.Of(dueDate!)));
                }
                if (released.HasValue)
                {
                    overrides.Add(("released", JsonBodyMerger.OverrideValue.Of(released.Value)));
                }

                var rawSource = hasRawSource
                    ? JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                    : BuildNestedTypedBase(queue);

                var body = JsonBodyMerger.Merge(rawSource, overrides)
                    ?? throw new TrackerException(ErrorCode.InvalidArgs,
                        "version create: provide --json-file/--json-stdin or typed flags.");

                using (var doc = JsonDocument.Parse(body))
                {
                    if (!doc.RootElement.TryGetProperty("queue", out _))
                    {
                        throw new TrackerException(ErrorCode.InvalidArgs,
                            "Effective body must include 'queue'.");
                    }
                    if (!doc.RootElement.TryGetProperty("name", out _))
                    {
                        throw new TrackerException(ErrorCode.InvalidArgs,
                            "Effective body must include 'name'.");
                    }
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
    /// Синтезирует базовый JSON-объект из <c>--queue</c>:
    /// <c>{"queue":{"key":...}}</c>. Возвращает <c>null</c>, если флаг не задан.
    /// </summary>
    /// <param name="queue">Значение опции <c>--queue</c>.</param>
    /// <returns>Сериализованный JSON-объект либо <c>null</c>.</returns>
    private static string? BuildNestedTypedBase(string? queue)
    {
        if (string.IsNullOrWhiteSpace(queue))
        {
            return null;
        }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteStartObject("queue");
            w.WriteString("key", queue);
            w.WriteEndObject();
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
