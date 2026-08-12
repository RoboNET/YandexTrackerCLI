namespace YandexTrackerCLI.Commands.Issue;

using System.CommandLine;
using System.Text.Json;
using Core.Api.Errors;
using Core.Config;
using Input;
using Output;

/// <summary>
/// Команда <c>yt issue batch</c>: пакетное изменение задач через
/// <c>POST /v3/bulkchange</c>. Тело запроса обязательно передаётся в raw-формате
/// (<c>--json-file</c> либо <c>--json-stdin</c>) — batch-payload слишком гибкий,
/// чтобы отражать его typed-аргументами.
/// </summary>
/// <remarks>
/// Если ни <c>--json-file</c>, ни <c>--json-stdin</c> не указаны, команда завершится
/// с кодом 2 (<see cref="ErrorCode.InvalidArgs"/>).
/// </remarks>
public static class IssueBatchCommand
{
    /// <summary>
    /// Строит subcommand <c>batch</c> для <c>yt issue</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var jsonFileOpt = new Option<string?>("--json-file") { Description = "Путь к JSON-файлу batch-операций." };
        var jsonStdinOpt = new Option<bool>("--json-stdin") { Description = "Читать batch-тело из stdin." };

        var cmd = new Command("batch", "Пакетное изменение задач (POST /v3/bulkchange).");
        cmd.Options.Add(jsonFileOpt);
        cmd.Options.Add(jsonStdinOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var jsonFile = pr.GetValue(jsonFileOpt);
                var jsonStdin = pr.GetValue(jsonStdinOpt);

                var body = JsonBodyReader.Read(jsonFile, jsonStdin, Console.In)
                    ?? throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "Batch requires --json-file or --json-stdin with the operations payload.");

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                EnsureBatchWithinAllowedQueues(body, ctx.Profile);

                var result = await ctx.Client.PostJsonRawAsync("bulkchange", body, ct);
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
    /// Проверяет batch-тело против политики <c>allowed_queues</c> профиля.
    /// </summary>
    /// <remarks>
    /// <c>POST /v3/bulkchange</c> адресует задачи телом запроса, а не URL, поэтому
    /// HTTP-guard здесь не работает. Две формы тела:
    /// <list type="bullet">
    ///   <item><description><c>{"issues":["DEV-1", ...]}</c> — проверяем очередь каждого ключа;</description></item>
    ///   <item><description><c>{"query":"..."}</c> — набор задач определяет сервер,
    ///   заранее ограничить его нельзя, поэтому при действующем ограничении такая форма
    ///   отклоняется целиком.</description></item>
    /// </list>
    /// <para>
    /// Поле <c>query</c> отклоняется независимо от наличия <c>issues</c>: иначе
    /// <c>{"issues":["DEV-1"],"query":"Queue: OPS"}</c> проходил бы проверку целиком —
    /// разрешённый ключ в списке ничего не говорит о том, что выберет сервер по запросу.
    /// </para>
    /// </remarks>
    /// <param name="body">Сырое JSON-тело batch-запроса.</param>
    /// <param name="profile">Действующий профиль.</param>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.PolicyViolation"/> — задача из запрещённой очереди или
    /// нелокализуемая query-форма при действующем ограничении.
    /// </exception>
    private static void EnsureBatchWithinAllowedQueues(string body, EffectiveProfile profile)
    {
        if (!QueuePolicy.IsRestricted(profile.AllowedQueues))
        {
            return;
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new TrackerException(
                ErrorCode.PolicyViolation,
                $"bulkchange payload cannot be checked against allowed_queues of profile '{profile.Name}'.");
        }

        if (doc.RootElement.TryGetProperty("query", out _))
        {
            throw new TrackerException(
                ErrorCode.PolicyViolation,
                $"bulkchange with a 'query' field is not allowed while allowed_queues "
                + $"is set on profile '{profile.Name}' (allowed: {string.Join(", ", profile.AllowedQueues!)}): "
                + "the server decides which issues the query selects, so they cannot be "
                + "verified before the request is sent.");
        }

        var checkedAny = false;
        if (doc.RootElement.TryGetProperty("issues", out var issues)
            && issues.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in issues.EnumerateArray())
            {
                var key = item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : QueueScopeFilter.ReadQueueKeyFromJson(item);
                QueueScopeFilter.EnsureTargetQueueAllowed(
                    QueuePolicy.QueueOfIssueKey(key), profile);
                checkedAny = true;
            }
        }

        if (!checkedAny)
        {
            throw new TrackerException(
                ErrorCode.PolicyViolation,
                $"bulkchange without an explicit 'issues' list is not allowed while allowed_queues "
                + $"is set on profile '{profile.Name}' (allowed: {string.Join(", ", profile.AllowedQueues!)}): "
                + "the affected issues cannot be verified before the request is sent.");
        }
    }
}
