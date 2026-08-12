namespace YandexTrackerCLI.Core.Http;

using Api.Errors;

/// <summary>
/// Delegating handler that blocks mutating HTTP methods (POST/PUT/PATCH/DELETE) when
/// the read-only policy is enabled. Safe methods (GET/HEAD/OPTIONS) are always passed through.
/// </summary>
public sealed class ReadOnlyGuardHandler : DelegatingHandler
{
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
        if (_enabled && MutatingRequest.IsMutating(request))
        {
            if (MutatingRequest.IsSafePostSearch(request))
            {
                return base.SendAsync(request, ct);
            }

            throw new TrackerException(
                ErrorCode.ReadOnlyMode,
                $"Blocked mutating request ({request.Method} {request.RequestUri}) by read-only policy.");
        }

        return base.SendAsync(request, ct);
    }
}
