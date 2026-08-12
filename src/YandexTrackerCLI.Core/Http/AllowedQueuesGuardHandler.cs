namespace YandexTrackerCLI.Core.Http;

using Config;

/// <summary>
/// Delegating handler that enforces the profile-level <c>allowed_queues</c> policy on the
/// final HTTP request. It inspects the already-assembled request URI, extracts the queue
/// key it addresses, and blocks the call when that queue is outside the profile's list.
/// </summary>
/// <remarks>
/// <para>
/// The guard sits at the HTTP layer on purpose: unlike a check in the command layer, it
/// cannot be side-stepped by an unusual invocation path — it sees the request that is
/// about to leave the process.
/// </para>
/// <para>Covered shapes:</para>
/// <list type="bullet">
///   <item><description><c>issues/{KEY}</c> and everything nested under it
///   (<c>comments</c>, <c>attachments</c>, <c>checklist</c>, <c>links</c>, <c>worklog</c>,
///   <c>transitions</c>, <c>changelog</c>, …) — the queue is the issue key prefix before the first dash.</description></item>
///   <item><description><c>queues/{KEY}</c> and everything nested under it
///   (automation triggers, autoactions, macros, …).</description></item>
///   <item><description>The <c>queue</c> query-string parameter (e.g. <c>issues/_suggest?queue=OPS</c>).</description></item>
/// </list>
/// <para>
/// Paths that carry no queue or issue key (<c>issues/_search</c>, <c>myself</c>, <c>users</c>,
/// <c>fields</c>, <c>boards</c>, …) pass through untouched: restricting free-form search
/// results is the job of the output-filtering layer in the CLI.
/// </para>
/// </remarks>
public sealed class AllowedQueuesGuardHandler : DelegatingHandler
{
    private readonly IReadOnlyList<string> _allowed;
    private readonly string _profileName;

    /// <summary>
    /// Initializes a new instance of the <see cref="AllowedQueuesGuardHandler"/> class.
    /// </summary>
    /// <param name="allowedQueues">
    /// Allowed queue keys. When <c>null</c> or empty, the handler is inert and every request passes through.
    /// </param>
    /// <param name="profileName">Profile name, used in the rejection message.</param>
    public AllowedQueuesGuardHandler(IReadOnlyList<string>? allowedQueues, string profileName)
    {
        _allowed = allowedQueues ?? Array.Empty<string>();
        _profileName = profileName;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!QueuePolicy.IsRestricted(_allowed))
        {
            return base.SendAsync(request, ct);
        }

        foreach (var queue in ExtractQueues(request.RequestUri))
        {
            if (!QueuePolicy.IsAllowed(_allowed, queue))
            {
                throw QueuePolicy.Denied(queue, _profileName, _allowed);
            }
        }

        return base.SendAsync(request, ct);
    }

    /// <summary>
    /// Extracts every queue key the request URI addresses: from the <c>issues/{KEY}</c> and
    /// <c>queues/{KEY}</c> path shapes and from a <c>queue</c> query-string parameter.
    /// </summary>
    /// <param name="uri">The request URI; may be <c>null</c> or relative.</param>
    /// <returns>Queue keys found in the URI; empty when the URI addresses no specific queue.</returns>
    internal static IReadOnlyList<string> ExtractQueues(Uri? uri)
    {
        if (uri is null)
        {
            return Array.Empty<string>();
        }

        var (path, query) = RequestUriPath.Split(uri);
        var result = new List<string>();

        var segments = RequestUriPath.Segments(path);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            // Segments arrive percent-encoded (commands escape keys with Uri.EscapeDataString);
            // decode before parsing so `DEV%2D1` or `%4FPS-1` cannot slip past the comparison.
            var segment = Decode(segments[i]);
            var isIssues = string.Equals(segment, "issues", StringComparison.OrdinalIgnoreCase);
            var isQueues = string.Equals(segment, "queues", StringComparison.OrdinalIgnoreCase);
            if (!isIssues && !isQueues)
            {
                continue;
            }

            var keySegment = Decode(segments[i + 1]);
            if (keySegment.Length == 0 || keySegment[0] == '_')
            {
                // API sub-resources such as `_search`, `_suggest`, `_count` carry no key.
                continue;
            }

            var queue = isIssues ? QueuePolicy.QueueOfIssueKey(keySegment) : keySegment;
            if (!string.IsNullOrWhiteSpace(queue))
            {
                result.Add(queue!);
            }
        }

        result.AddRange(ReadQueueQueryParameters(query));

        return result;
    }

    /// <summary>
    /// Reads <b>every</b> <c>queue</c> query-string parameter.
    /// </summary>
    /// <remarks>
    /// All occurrences are collected on purpose. Stopping at the first match would let
    /// <c>?queue=DEV&amp;queue=OPS</c> be checked as <c>DEV</c> alone while the server may well
    /// honour the second value. No command builds such a URL today, but the guard protects the
    /// request that is about to leave the process, not the commands it happens to trust.
    /// </remarks>
    /// <param name="query">The raw query string, including the leading <c>?</c>.</param>
    /// <returns>The decoded queue keys; empty when the parameter is absent or empty.</returns>
    private static IReadOnlyList<string> ReadQueueQueryParameters(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            if (!string.Equals(Decode(pair[..eq]), "queue", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = Decode(pair[(eq + 1)..]);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static string Decode(string value) => RequestUriPath.Decode(value);
}
