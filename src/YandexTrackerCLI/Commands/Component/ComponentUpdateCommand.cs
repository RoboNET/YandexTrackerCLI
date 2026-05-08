namespace YandexTrackerCLI.Commands.Component;

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Input;
using Output;

/// <summary>
/// Команда <c>yt component update &lt;id&gt;</c>: обновляет компонент через
/// <c>PATCH /v3/components/{id}</c>. Тело собирается через
/// <see cref="JsonBodyReader.ReadAndMerge"/>: scalar-флаги
/// (<c>--name</c>, <c>--description</c>, <c>--assign-auto</c>) мерджатся
/// поверх raw-payload. Nested-флаг <c>--lead</c> поддерживается только при
/// отсутствии raw-источника и собирает синтетический base-payload с обёрткой
/// <c>{"login":...}</c>.
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
        var nameOpt = new Option<string?>("--name") { Description = "Новое название (override поля name)." };
        var descriptionOpt = new Option<string?>("--description") { Description = "Новое описание (override поля description)." };
        var leadOpt = new Option<string?>("--lead")
        {
            Description = "Логин нового владельца (синтезирует {\"lead\":{\"login\":...}}; не сочетается с --json-*).",
        };
        var assignAutoOpt = new Option<bool>("--assign-auto") { Description = "Установить assignAuto=true (override)." };
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу с телом запроса." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать JSON-тело из stdin." };

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

                var hasNestedTyped = !string.IsNullOrWhiteSpace(lead);
                var hasRawSource = !string.IsNullOrWhiteSpace(jsonFile) || jsonStdin;

                if (hasNestedTyped && hasRawSource)
                {
                    throw new TrackerException(ErrorCode.InvalidArgs,
                        "component update: --lead cannot be combined with --json-file/--json-stdin (use a JSON object instead).");
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
                    : BuildNestedTypedBase(lead);

                var body = JsonBodyMerger.Merge(rawSource, overrides)
                    ?? throw new TrackerException(ErrorCode.InvalidArgs,
                        "component update: nothing to update (provide typed flags or --json-file/--json-stdin).");

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
    /// Синтезирует базовый JSON-объект из nested-typed-флага <c>--lead</c>:
    /// <c>{"lead":{"login":...}}</c>. Возвращает <c>null</c>, если флаг не задан.
    /// </summary>
    /// <param name="lead">Значение опции <c>--lead</c>.</param>
    /// <returns>Сериализованный JSON-объект либо <c>null</c>.</returns>
    private static string? BuildNestedTypedBase(string? lead)
    {
        if (string.IsNullOrWhiteSpace(lead))
        {
            return null;
        }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteStartObject("lead");
            w.WriteString("login", lead);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
