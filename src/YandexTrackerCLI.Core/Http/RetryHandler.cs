namespace YandexTrackerCLI.Core.Http;

/// <summary>
/// Delegating handler that retries transient HTTP failures with exponential backoff.
/// Retries on HTTP <c>429 Too Many Requests</c>, any <c>5xx</c> status, and transient
/// <see cref="HttpRequestException"/>. Respects the <c>Retry-After</c> header when present.
/// Non-transient failures (including 4xx other than 429) are returned immediately.
/// Requests whose body cannot be safely replayed (e.g. <see cref="MultipartContent"/> or
/// <see cref="StreamContent"/> over a non-seekable source) are never retried, because
/// the underlying stream has already been consumed by the first attempt and a second
/// send would transmit an empty body.
/// </summary>
public sealed class RetryHandler : DelegatingHandler
{
    private readonly int _maxAttempts;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _cap;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryHandler"/> class.
    /// </summary>
    /// <param name="maxAttempts">Total attempts including the initial call. Must be at least 1.</param>
    /// <param name="baseDelay">Base delay used for exponential backoff (doubled each attempt).</param>
    /// <param name="cap">Upper bound applied to every computed or <c>Retry-After</c> delay.</param>
    public RetryHandler(int maxAttempts = 3, TimeSpan? baseDelay = null, TimeSpan? cap = null)
    {
        _maxAttempts = maxAttempts;
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
        _cap = cap ?? TimeSpan.FromSeconds(10);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var canRetryContent = CanSafelyRetryContent(request.Content);
        var attempt = 0;
        while (true)
        {
            HttpResponseMessage resp;
            try
            {
                resp = await base.SendAsync(request, ct);
            }
            catch (HttpRequestException) when (canRetryContent && attempt < _maxAttempts - 1)
            {
                await Task.Delay(Backoff(attempt), ct);
                attempt++;
                continue;
            }

            var code = (int)resp.StatusCode;
            var shouldRetry = canRetryContent
                              && attempt < _maxAttempts - 1
                              && (code == 429 || (code >= 500 && code <= 599));
            if (!shouldRetry)
            {
                return resp;
            }

            var delay = resp.Headers.RetryAfter?.Delta ?? Backoff(attempt);
            resp.Dispose();
            if (delay > _cap)
            {
                delay = _cap;
            }

            await Task.Delay(delay, ct);
            attempt++;
        }
    }

    /// <summary>
    /// Determines whether the given request content can be safely re-sent by a retry.
    /// </summary>
    /// <param name="content">The request content to evaluate, or <c>null</c>.</param>
    /// <returns>
    /// <c>true</c> if the content is <c>null</c> or a buffered type (e.g.
    /// <see cref="StringContent"/>, <see cref="ByteArrayContent"/>); <c>false</c> for
    /// <see cref="MultipartContent"/> and <see cref="StreamContent"/>, whose underlying
    /// streams are consumed on the first send and cannot be reliably replayed.
    /// </returns>
    private static bool CanSafelyRetryContent(HttpContent? content)
    {
        if (content is null)
        {
            return true;
        }

        // MultipartContent (and its MultipartFormDataContent subclass) wraps inner
        // HttpContent parts — often StreamContent over a FileStream — which the
        // serialization pipeline reads once. A retry would transmit an empty body.
        if (content is MultipartContent)
        {
            return false;
        }

        // StreamContent reads its underlying stream once; the stream may be
        // non-seekable (NetworkStream, DeflateStream) and StreamContent does not
        // expose a public way to inspect or rewind it. Be conservative and never
        // retry StreamContent — callers that need retry must buffer into a byte[]
        // or string first.
        if (content is StreamContent)
        {
            return false;
        }

        // StringContent, ByteArrayContent (and derivatives like JsonContent) buffer
        // their payload in memory and can be re-serialized safely.
        return true;
    }

    private TimeSpan Backoff(int attempt)
    {
        var ms = _baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var capped = Math.Min(ms, _cap.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(capped);
    }
}
