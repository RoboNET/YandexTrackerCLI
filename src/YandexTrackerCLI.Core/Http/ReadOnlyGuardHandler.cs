namespace YandexTrackerCLI.Core.Http;

using Api.Errors;

/// <summary>
/// Delegating handler that blocks mutating HTTP methods (POST/PUT/PATCH/DELETE) when
/// the read-only policy is enabled. Safe methods (GET/HEAD/OPTIONS) are always passed through.
/// </summary>
public sealed class ReadOnlyGuardHandler : DelegatingHandler
{
    private static readonly HashSet<string> Mutating = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE",
    };

    private readonly bool _enabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReadOnlyGuardHandler"/> class.
    /// </summary>
    /// <param name="enabled">
    /// When <c>true</c>, mutating requests are blocked with a <see cref="TrackerException"/>
    /// of code <see cref="ErrorCode.ReadOnlyMode"/>. When <c>false</c>, all requests pass through.
    /// </param>
    public ReadOnlyGuardHandler(bool enabled) => _enabled = enabled;

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (_enabled && Mutating.Contains(request.Method.Method))
        {
            if (IsReadOnlyPostSearch(request))
            {
                return base.SendAsync(request, ct);
            }

            throw new TrackerException(
                ErrorCode.ReadOnlyMode,
                $"Blocked mutating request ({request.Method} {request.RequestUri}) by read-only policy.");
        }

        return base.SendAsync(request, ct);
    }

    /// <summary>
    /// Determines whether the request is a Yandex Tracker search endpoint invocation that
    /// uses <c>POST</c> semantically as a read-only operation (e.g. <c>/v3/issues/_search</c>,
    /// <c>/v3/entities/project/_search</c>). Such calls carry a JSON query in the body but
    /// do not mutate server state, so they must pass through even under a read-only policy.
    /// </summary>
    /// <param name="request">The outgoing HTTP request.</param>
    /// <returns>
    /// <c>true</c> if the request method is <c>POST</c> and its path ends with <c>/_search</c>;
    /// otherwise <c>false</c>.
    /// </returns>
    private static bool IsReadOnlyPostSearch(HttpRequestMessage request)
    {
        if (!HttpMethod.Post.Equals(request.Method))
        {
            return false;
        }

        var path = request.RequestUri?.AbsolutePath;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return path.EndsWith("/_search", StringComparison.Ordinal);
    }
}
