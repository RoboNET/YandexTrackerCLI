namespace YandexTrackerCLI.Core.Http;

using System.Text.Json;
using Api.Errors;
using Config;

/// <summary>
/// Delegating handler that enforces the profile-level <c>external_effects</c> policy:
/// with the policy off the process may not <i>explicitly</i> initiate mail-outs and
/// integrations — it may neither summon people (<c>summonees</c>,
/// <c>maillistSummonees</c> anywhere in the JSON body) nor mutate queue automations
/// (<c>queues/{KEY}/triggers</c>, <c>.../autoactions</c>, <c>.../macros</c> — the whole
/// <c>yt automation</c> group). Reading stays unrestricted.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not a guarantee that nothing leaves the installation.</b> Any issue edit can
/// fire a trigger already configured on the queue side, and that trigger will send a mail or
/// an outbound HTTP request; the CLI neither knows about it nor can influence it. The policy
/// is worded as «do not initiate mail-outs and integrations explicitly» for exactly that
/// reason: it covers what the caller itself orders — the notification the request body asks
/// for, and the creation or change of an automation that outlives the session. The only
/// policy that guarantees silence is <c>read_only</c>.
/// </para>
/// <para>
/// The guard sits at the HTTP layer for the same reason as
/// <see cref="AllowedWriteIssuesGuardHandler"/>: the point of a policy is that the caller
/// cannot talk the CLI out of it. A check in the command layer would only see the arguments
/// the command happens to parse, while <c>--json-file</c>/<c>--json-stdin</c> bodies —
/// including <c>bulkchange</c> payloads — go straight to the wire.
/// </para>
/// <para>Decision table with the policy off:</para>
/// <list type="bullet">
///   <item><description>GET/HEAD/OPTIONS — always pass through, including reads of
///   <c>triggers</c>/<c>autoactions</c>/<c>macros</c>: the policy restricts writes only.</description></item>
///   <item><description><c>POST .../_search</c> — passes through: semantically a read
///   (the same exemption <see cref="ReadOnlyGuardHandler"/> makes).</description></item>
///   <item><description>POST/PUT/PATCH/DELETE on <c>queues/{KEY}/{triggers|autoactions|macros}</c>
///   (percent-decoded, compared case-insensitively, matched positionally so that a queue whose
///   key happens to be <c>TRIGGERS</c> is not affected) — denied.</description></item>
///   <item><description>A mutating request with no URI at all — denied: an unverifiable
///   request is not a safe one.</description></item>
///   <item><description>Any mutating request whose JSON body carries <c>summonees</c> or
///   <c>maillistSummonees</c> at any depth — denied, wherever the field sits (a comment, a
///   nested comment inside a transition, a <c>bulkchange</c> payload).</description></item>
///   <item><description>A body declared as JSON that cannot be inspected (over the size
///   limit, or malformed) — denied: it cannot be proven safe.</description></item>
/// </list>
/// </remarks>
public sealed class ExternalEffectsGuardHandler : DelegatingHandler
{
    /// <summary>
    /// Upper bound for buffering a request body before inspecting it, mirroring
    /// <see cref="AllowedWriteIssuesGuardHandler"/>: Tracker payloads the CLI sends as JSON
    /// are orders of magnitude smaller, and anything above the limit is refused rather than
    /// buffered, so the guard cannot be turned into a memory sink.
    /// </summary>
    private const int MaxInspectedBodyBytes = 4 * 1024 * 1024;

    private readonly bool _allowed;
    private readonly string _profileName;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalEffectsGuardHandler"/> class.
    /// </summary>
    /// <param name="externalEffectsAllowed">
    /// Effective <c>external_effects</c> policy. When <c>true</c> the handler is inert and
    /// every request passes through.
    /// </param>
    /// <param name="profileName">Profile name, used in the rejection message.</param>
    public ExternalEffectsGuardHandler(bool externalEffectsAllowed, string profileName)
    {
        _allowed = externalEffectsAllowed;
        _profileName = profileName;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (_allowed
            || !MutatingRequest.IsMutating(request)
            || MutatingRequest.IsSafePostSearch(request))
        {
            return await base.SendAsync(request, ct).ConfigureAwait(false);
        }

        EnsureNoAutomationMutation(request);
        await EnsureBodyCarriesNoSummon(request, ct).ConfigureAwait(false);

        return await base.SendAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Rejects mutations of queue automations
    /// (<c>queues/{KEY}/{triggers|autoactions|macros}</c>).
    /// </summary>
    /// <param name="request">The outgoing request.</param>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.PolicyViolation"/> when the path addresses an automation resource,
    /// or when the request carries no URI to check.
    /// </exception>
    private void EnsureNoAutomationMutation(HttpRequestMessage request)
    {
        if (request.RequestUri is null)
        {
            // Мутирующий запрос без URI проверить нельзя, а «не знаю» в барьере обязано
            // означать отказ, а не пропуск: то же решение принимает
            // AllowedWriteIssuesGuardHandler, когда из пути не извлекается ни один ключ.
            throw new TrackerException(
                ErrorCode.PolicyViolation,
                $"mutating {request.Method.Method} request carries no URI, so it cannot be "
                + $"verified against external_effects of profile '{_profileName}'");
        }

        var (path, _) = RequestUriPath.Split(request.RequestUri);
        var decoded = Array.ConvertAll(RequestUriPath.Segments(path), RequestUriPath.Decode);

        var segment = ExternalEffectsPolicy.FindAutomationSegment(decoded);
        if (segment is not null)
        {
            throw ExternalEffectsPolicy.DeniedAutomation(segment, DescribeOperation(request), _profileName);
        }
    }

    /// <summary>
    /// Rejects the request when its JSON body summons people or mailing lists.
    /// </summary>
    /// <remarks>
    /// Only JSON bodies are inspected: attachment uploads travel as multipart/binary content
    /// and buffering them would be both pointless and expensive. The body is loaded into the
    /// content's own buffer first, so the subsequent send still has it.
    /// </remarks>
    /// <param name="request">The outgoing request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.PolicyViolation"/> when a summon field is present, or when the
    /// body is JSON but cannot be inspected (too large, or malformed).
    /// </exception>
    private async Task EnsureBodyCarriesNoSummon(HttpRequestMessage request, CancellationToken ct)
    {
        var content = request.Content;
        if (content is null || !IsJson(content))
        {
            return;
        }

        try
        {
            await content.LoadIntoBufferAsync(MaxInspectedBodyBytes, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw ExternalEffectsPolicy.DeniedUninspectableBody("is too large", _profileName);
        }

        var body = await content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            // Body declared as JSON but unparseable: with the policy on we cannot prove it
            // carries no summon, so it does not go out.
            throw ExternalEffectsPolicy.DeniedUninspectableBody("could not be parsed as JSON", _profileName);
        }

        using (doc)
        {
            var field = ExternalEffectsPolicy.FindSummonField(doc.RootElement);
            if (field is not null)
            {
                throw ExternalEffectsPolicy.DeniedSummon(field, _profileName);
            }
        }
    }

    private static bool IsJson(HttpContent content)
    {
        var mediaType = content.Headers.ContentType?.MediaType;
        return mediaType is not null
               && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeOperation(HttpRequestMessage request)
    {
        if (request.RequestUri is null)
        {
            return request.Method.Method;
        }

        var (path, _) = RequestUriPath.Split(request.RequestUri);
        return $"{request.Method.Method} {path.TrimStart('/')}";
    }
}
