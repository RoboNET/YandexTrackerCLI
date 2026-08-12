namespace YandexTrackerCLI.Core.Http;

using System.Text.Json;
using Api.Errors;
using Config;

/// <summary>
/// Delegating handler that enforces the profile-level <c>allowed_write_issues</c> policy:
/// under an active restriction the process may only write into the listed issues, while
/// reading stays unrestricted.
/// </summary>
/// <remarks>
/// <para>
/// The guard sits at the HTTP layer for the same reason as
/// <see cref="AllowedQueuesGuardHandler"/>: the whole point of the policy is that the
/// caller cannot talk the CLI out of it. A check in the command layer would only cover the
/// arguments the command happens to parse, and the body of <c>--json-file</c>/<c>--json-stdin</c>
/// is precisely what an injected instruction would use.
/// </para>
/// <para>Decision table under an active restriction:</para>
/// <list type="bullet">
///   <item><description>GET/HEAD/OPTIONS — always pass through: the policy restricts writes only.</description></item>
///   <item><description><c>POST .../_search</c> — passes through: semantically a read
///   (the same exemption <see cref="ReadOnlyGuardHandler"/> makes).</description></item>
///   <item><description>POST/PUT/PATCH/DELETE on <c>issues/{KEY}/...</c> — allowed only when
///   <c>KEY</c> is in the list (percent-decoded, compared case-insensitively).</description></item>
///   <item><description>Every other mutating request — denied, including issue creation
///   (<c>POST issues</c>), <c>POST bulkchange</c> and queue/automation mutations
///   (<c>queues/{KEY}/triggers</c>, …). Default deny: only a proven key match opens the door.</description></item>
///   <item><description>A JSON body carrying <c>summonees</c>/<c>maillistSummonees</c> at any
///   depth — denied: those fields notify recipients outside the issue.</description></item>
/// </list>
/// </remarks>
public sealed class AllowedWriteIssuesGuardHandler : DelegatingHandler
{
    /// <summary>
    /// Upper bound for buffering a request body before inspecting it. Tracker payloads that
    /// the CLI sends as JSON are orders of magnitude smaller; anything above the limit is
    /// refused rather than buffered, so the guard cannot be turned into a memory sink.
    /// </summary>
    private const int MaxInspectedBodyBytes = 4 * 1024 * 1024;

    private readonly IReadOnlyList<string> _allowed;
    private readonly string _profileName;

    /// <summary>
    /// Initializes a new instance of the <see cref="AllowedWriteIssuesGuardHandler"/> class.
    /// </summary>
    /// <param name="allowedWriteIssues">
    /// Issue keys open for writing. When <c>null</c> or empty, the handler is inert and every
    /// request passes through.
    /// </param>
    /// <param name="profileName">Profile name, used in the rejection message.</param>
    public AllowedWriteIssuesGuardHandler(IReadOnlyList<string>? allowedWriteIssues, string profileName)
    {
        _allowed = allowedWriteIssues ?? Array.Empty<string>();
        _profileName = profileName;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!IssueWritePolicy.IsRestricted(_allowed)
            || !MutatingRequest.IsMutating(request)
            || MutatingRequest.IsSafePostSearch(request))
        {
            return await base.SendAsync(request, ct).ConfigureAwait(false);
        }

        var keys = ExtractIssueKeys(request.RequestUri);
        if (keys.Count == 0)
        {
            throw IssueWritePolicy.DeniedUnscoped(DescribeOperation(request), _profileName, _allowed);
        }

        foreach (var key in keys)
        {
            if (!IssueWritePolicy.IsAllowed(_allowed, key))
            {
                throw IssueWritePolicy.Denied(key, _profileName, _allowed);
            }
        }

        await EnsureBodyCarriesNoNotificationFields(request, ct).ConfigureAwait(false);

        return await base.SendAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Extracts every issue key the request URI addresses via the <c>issues/{KEY}</c> shape.
    /// </summary>
    /// <param name="uri">The request URI; may be <c>null</c> or relative.</param>
    /// <returns>
    /// Issue keys found in the path; empty when the URI addresses no specific issue
    /// (which, under an active restriction, means the request is denied).
    /// </returns>
    internal static IReadOnlyList<string> ExtractIssueKeys(Uri? uri)
    {
        if (uri is null)
        {
            return Array.Empty<string>();
        }

        var (path, _) = RequestUriPath.Split(uri);
        var segments = RequestUriPath.Segments(path);
        var result = new List<string>();

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (!string.Equals(RequestUriPath.Decode(segments[i]), "issues", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = RequestUriPath.Decode(segments[i + 1]);
            if (key.Length == 0 || key[0] == '_')
            {
                // API sub-resources such as `_search`, `_suggest`, `_count` carry no key.
                continue;
            }

            result.Add(key);
        }

        return result;
    }

    /// <summary>
    /// Rejects the request when its JSON body carries a field that notifies recipients
    /// outside the issue (<c>summonees</c>, <c>maillistSummonees</c>).
    /// </summary>
    /// <remarks>
    /// Only JSON bodies are inspected: attachment uploads travel as multipart/binary content
    /// and buffering them would be both pointless and expensive. The body is loaded into the
    /// content's own buffer first, so the subsequent send still has it.
    /// </remarks>
    /// <param name="request">The outgoing request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.PolicyViolation"/> when such a field is present, or when the body
    /// is JSON but cannot be inspected (too large, or malformed).
    /// </exception>
    private async Task EnsureBodyCarriesNoNotificationFields(HttpRequestMessage request, CancellationToken ct)
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
            throw new TrackerException(
                ErrorCode.PolicyViolation,
                $"request body is too large to verify against allowed_write_issues of profile '{_profileName}'.");
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
            // Body declared as JSON but unparseable: under an active restriction we cannot
            // prove it is safe, so it does not go out.
            throw new TrackerException(
                ErrorCode.PolicyViolation,
                $"request body could not be parsed as JSON and is blocked by allowed_write_issues of profile '{_profileName}'.");
        }

        using (doc)
        {
            var field = IssueWritePolicy.FindNotificationField(doc.RootElement);
            if (field is not null)
            {
                throw IssueWritePolicy.DeniedNotificationField(field, _profileName);
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
