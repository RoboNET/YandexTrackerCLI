namespace YandexTrackerCLI.Commands.Component;

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt component create</c>: создаёт компонент через
/// <c>POST /v3/components</c>. Тело собирается из источника
/// (<c>--json-file</c>/<c>--json-stdin</c>) и inline-флагов через
/// <see cref="JsonBodyReader.ReadAndMerge"/>: scalar-флаги
/// (<c>--name</c>, <c>--description</c>, <c>--assign-auto</c>) мерджатся
/// поверх raw-payload. Nested-флаги (<c>--queue</c>, <c>--lead</c>)
/// поддерживаются только при отсутствии raw-источника и собирают
/// синтетический base-payload с обёртками <c>{"key":...}</c> и
/// <c>{"login":...}</c>.
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
            Description = "Ключ очереди (синтезирует {\"queue\":{\"key\":...}}; не сочетается с --json-*).",
        };
        var nameOpt = new Option<string?>("--name") { Description = "Название компонента (override поля name)." };
        var descriptionOpt = new Option<string?>("--description") { Description = "Описание (override поля description)." };
        var leadOpt = new Option<string?>("--lead")
        {
            Description = "Логин владельца (синтезирует {\"lead\":{\"login\":...}}; не сочетается с --json-*).",
        };
        var assignAutoOpt = new Option<bool>("--assign-auto") { Description = "Автоназначение (override поля assignAuto=true)." };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу с телом запроса." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

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

                var hasNestedTyped = !string.IsNullOrWhiteSpace(queue) || !string.IsNullOrWhiteSpace(lead);
                var hasRawSource = !string.IsNullOrWhiteSpace(jsonFile) || jsonStdin;

                if (hasNestedTyped && hasRawSource)
                {
                    throw new TrackerException(ErrorCode.InvalidArgs,
                        "component create: --queue/--lead cannot be combined with --json-file/--json-stdin (use a JSON object instead).");
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
                if (assignAuto)
                {
                    overrides.Add(("assignAuto", JsonBodyMerger.OverrideValue.Of(true)));
                }

                var rawSource = hasRawSource
                    ? JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                    : BuildNestedTypedBase(queue, lead);

                var body = JsonBodyMerger.Merge(rawSource, overrides)
                    ?? throw new TrackerException(ErrorCode.InvalidArgs,
                        "component create: provide --json-file/--json-stdin or typed flags.");

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
    /// Синтезирует базовый JSON-объект только из nested-typed-флагов
    /// (<c>--queue</c> -> <c>{"queue":{"key":...}}</c>, <c>--lead</c> -> <c>{"lead":{"login":...}}</c>).
    /// Возвращает <c>null</c>, если ни один nested-флаг не задан.
    /// </summary>
    /// <param name="queue">Значение опции <c>--queue</c>.</param>
    /// <param name="lead">Значение опции <c>--lead</c>.</param>
    /// <returns>Сериализованный JSON-объект либо <c>null</c>.</returns>
    private static string? BuildNestedTypedBase(string? queue, string? lead)
    {
        if (string.IsNullOrWhiteSpace(queue) && string.IsNullOrWhiteSpace(lead))
        {
            return null;
        }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            if (!string.IsNullOrWhiteSpace(queue))
            {
                w.WriteStartObject("queue");
                w.WriteString("key", queue);
                w.WriteEndObject();
            }
            if (!string.IsNullOrWhiteSpace(lead))
            {
                w.WriteStartObject("lead");
                w.WriteString("login", lead);
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
