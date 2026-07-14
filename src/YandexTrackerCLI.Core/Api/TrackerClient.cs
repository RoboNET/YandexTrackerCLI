namespace YandexTrackerCLI.Core.Api;

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Errors;

/// <summary>
/// Facade over <see cref="HttpClient"/> that performs JSON requests against the Yandex Tracker API
/// and maps HTTP status codes into <see cref="TrackerException"/> with a well-defined <see cref="ErrorCode"/>.
/// </summary>
public sealed class TrackerClient
{
    private readonly HttpClient _http;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrackerClient"/> class.
    /// </summary>
    /// <param name="http">The <see cref="HttpClient"/> configured with base address and auth headers.</param>
    public TrackerClient(HttpClient http) => _http = http;

    /// <summary>
    /// Sends a GET request to the specified path and returns the parsed JSON response.
    /// </summary>
    /// <param name="pathAndQuery">Path and query relative to the client's base address.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The root <see cref="JsonElement"/> of the response body.</returns>
    /// <exception cref="TrackerException">Thrown when the response indicates a failure status code.</exception>
    public async Task<JsonElement> GetAsync(string pathAndQuery, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(pathAndQuery, ct);
        return await ReadJsonOrThrow(resp, ct);
    }

    /// <summary>
    /// Sends a POST request with a JSON body to the specified path and returns the parsed JSON response.
    /// </summary>
    /// <param name="pathAndQuery">Path and query relative to the client's base address.</param>
    /// <param name="body">The JSON body to serialize into the request content.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The root <see cref="JsonElement"/> of the response body.</returns>
    /// <exception cref="TrackerException">Thrown when the response indicates a failure status code.</exception>
    public async Task<JsonElement> PostJsonAsync(string pathAndQuery, JsonElement body, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, pathAndQuery);
        req.Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct);
        return await ReadJsonOrThrow(resp, ct);
    }

    /// <summary>
    /// Sends a POST request with a raw JSON string body to the specified path and returns the parsed JSON response.
    /// </summary>
    /// <param name="pathAndQuery">Path and query relative to the client's base address.</param>
    /// <param name="rawJson">The raw JSON string to send as the request body.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The root <see cref="JsonElement"/> of the response body.</returns>
    /// <exception cref="TrackerException">Thrown when the response indicates a failure status code.</exception>
    public async Task<JsonElement> PostJsonRawAsync(string pathAndQuery, string rawJson, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, pathAndQuery);
        req.Content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct);
        return await ReadJsonOrThrow(resp, ct);
    }

    /// <summary>
    /// Sends a POST request with a raw JSON body and returns both the parsed response and the
    /// value of the <c>X-Total-Pages</c> response header (defaults to <c>1</c> when absent or malformed).
    /// </summary>
    /// <param name="pathAndQuery">Path and query relative to the client's base address.</param>
    /// <param name="rawJson">The raw JSON string to send as the request body.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A tuple with the response body and the total number of pages.</returns>
    /// <exception cref="TrackerException">Thrown when the response indicates a failure status code.</exception>
    public async Task<(JsonElement Body, int TotalPages)> PostJsonRawWithHeadersAsync(
        string pathAndQuery,
        string rawJson,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, pathAndQuery);
        req.Content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct);
        var body = await ReadJsonOrThrow(resp, ct);

        var total = 1;
        if (resp.Headers.TryGetValues("X-Total-Pages", out var values)
            && int.TryParse(values.FirstOrDefault(), out var parsed))
        {
            total = parsed;
        }

        return (body, total);
    }

    /// <summary>
    /// Sends a PATCH request with a raw JSON string body to the specified path and returns the parsed JSON response.
    /// </summary>
    /// <param name="pathAndQuery">Path and query relative to the client's base address.</param>
    /// <param name="rawJson">The raw JSON string to send as the request body.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The root <see cref="JsonElement"/> of the response body.</returns>
    /// <exception cref="TrackerException">Thrown when the response indicates a failure status code.</exception>
    public async Task<JsonElement> PatchJsonAsync(string pathAndQuery, string rawJson, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, pathAndQuery);
        req.Content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct);
        return await ReadJsonOrThrow(resp, ct);
    }

    /// <summary>
    /// Sends a DELETE request without a body to the specified path and returns the parsed JSON response.
    /// Returns a default <see cref="JsonElement"/> (<see cref="JsonValueKind.Undefined"/>) when the response body is empty.
    /// </summary>
    /// <param name="pathAndQuery">Path and query relative to the client's base address.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The root <see cref="JsonElement"/> of the response body, or <see langword="default"/> when empty.</returns>
    /// <exception cref="TrackerException">Thrown when the response indicates a failure status code.</exception>
    public async Task<JsonElement> DeleteAsync(string pathAndQuery, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, pathAndQuery);
        using var resp = await _http.SendAsync(req, ct);
        return await ReadJsonOrThrow(resp, ct);
    }

    /// <summary>
    /// Sends a POST request with <see cref="MultipartFormDataContent"/> (e.g., streaming file uploads)
    /// to the specified path and returns the parsed JSON response.
    /// </summary>
    /// <param name="pathAndQuery">Path and query relative to the client's base address.</param>
    /// <param name="content">The multipart form data content to send as the request body.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The root <see cref="JsonElement"/> of the response body.</returns>
    /// <exception cref="TrackerException">Thrown when the response indicates a failure status code.</exception>
    public async Task<JsonElement> PostMultipartAsync(
        string pathAndQuery,
        MultipartFormDataContent content,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, pathAndQuery);
        req.Content = content;
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        return await ReadJsonOrThrow(resp, ct);
    }

    /// <summary>
    /// Sends a GET request and returns a streaming <see cref="TrackerDownload"/> that owns the response
    /// and exposes the body as a <see cref="Stream"/>. Use for binary attachments or any non-JSON payloads.
    /// </summary>
    /// <param name="pathAndQuery">Path and query relative to the client's base address.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A <see cref="TrackerDownload"/> whose stream must be disposed by the caller.</returns>
    /// <exception cref="TrackerException">Thrown when the response indicates a failure status code.</exception>
    public async Task<TrackerDownload> GetStreamingAsync(string pathAndQuery, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(pathAndQuery, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            try
            {
                await ReadJsonOrThrow(resp, ct);
            }
            finally
            {
                resp.Dispose();
            }

            throw new InvalidOperationException("unreachable");
        }

        var stream = await resp.Content.ReadAsStreamAsync(ct);
        return new TrackerDownload(resp, stream);
    }

    /// <summary>
    /// Iterates a paged collection endpoint, yielding each item across all pages.
    /// When the response body is not a JSON array, the single element is yielded once.
    /// </summary>
    /// <param name="basePath">Path and optional query relative to the client's base address.</param>
    /// <param name="perPage">Items per page; appended as <c>perPage</c> query parameter.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>An async stream of JSON elements representing items in the collection.</returns>
    /// <exception cref="TrackerException">Thrown when any page request fails.</exception>
    public async IAsyncEnumerable<JsonElement> GetPagedAsync(
        string basePath,
        int perPage = 50,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var page = 1;
        while (true)
        {
            var sep = basePath.Contains('?') ? '&' : '?';
            using var resp = await _http.GetAsync($"{basePath}{sep}perPage={perPage}&page={page}", ct);
            var payload = await ReadJsonOrThrow(resp, ct);

            if (payload.ValueKind != JsonValueKind.Array)
            {
                yield return payload;
                yield break;
            }

            foreach (var item in payload.EnumerateArray())
            {
                yield return item;
            }

            if (!resp.Headers.TryGetValues("X-Total-Pages", out var totalPagesHeader)
                || !int.TryParse(totalPagesHeader.FirstOrDefault(), out var total)
                || page >= total)
            {
                yield break;
            }

            page++;
        }
    }

    /// <summary>
    /// Iterates a cursor-paginated collection endpoint (e.g. <c>issues/{key}/changelog</c>) by following
    /// the <c>Link</c> response header with <c>rel="next"</c> (RFC 5988) until no further page exists.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="GetPagedAsync"/> (which relies on <c>page</c>/<c>X-Total-Pages</c>), the Tracker
    /// changelog endpoint paginates with an opaque cursor: each response carries a <c>Link</c> header whose
    /// <c>next</c> URL embeds the next cursor (<c>?id=&lt;cursor&gt;&amp;perPage=N</c>). The absolute URL from the
    /// header is reduced to a path relative to the client's <c>/v3/</c> base address before the next request.
    /// </remarks>
    /// <param name="basePath">Collection path relative to the base address (without leading slash).</param>
    /// <param name="perPage">Page size requested for the first page; subsequent pages follow the server cursor.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An asynchronous sequence of the collection's elements across all pages.</returns>
    /// <exception cref="TrackerException">Thrown when any page response indicates a failure status code.</exception>
    public async IAsyncEnumerable<JsonElement> GetCursorPagedAsync(
        string basePath,
        int perPage = 50,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sep = basePath.Contains('?') ? '&' : '?';
        var requestUri = $"{basePath}{sep}perPage={perPage}";

        while (true)
        {
            string? nextUri;
            JsonElement payload;
            using (var resp = await _http.GetAsync(requestUri, ct))
            {
                payload = await ReadJsonOrThrow(resp, ct);
                nextUri = resp.Headers.TryGetValues("Link", out var links)
                    ? ResolveNextLink(links)
                    : null;
            }

            if (payload.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in payload.EnumerateArray())
                {
                    yield return item;
                }
            }
            else if (payload.ValueKind != JsonValueKind.Undefined)
            {
                yield return payload;
            }

            if (nextUri is null)
            {
                yield break;
            }

            // Guard against a non-advancing cursor: some server responses keep returning a
            // "next" link identical to the current request (optionally with empty payloads),
            // which would otherwise spin this loop issuing the same HTTP request forever.
            if (string.Equals(nextUri, requestUri, StringComparison.Ordinal))
            {
                yield break;
            }

            requestUri = nextUri;
        }
    }

    /// <summary>
    /// Parses one or more <c>Link</c> header values (RFC 5988) and returns the target of the
    /// <c>rel="next"</c> relation as a path relative to the client's <c>/v3/</c> base address,
    /// or <see langword="null"/> when no <c>next</c> link is present.
    /// </summary>
    /// <param name="linkHeaders">Raw <c>Link</c> header values; each may itself hold comma-separated links.</param>
    /// <returns>Relative path + query for the next page, or <see langword="null"/>.</returns>
    private static string? ResolveNextLink(IEnumerable<string> linkHeaders)
    {
        foreach (var header in linkHeaders)
        {
            // A single Link header value can hold several comma-separated links, e.g.
            // <https://.../changelog?id=1&perPage=100>; rel="next", <https://.../changelog?id=0>; rel="prev"
            foreach (var part in header.Split(','))
            {
                var segment = part.Trim();
                var open = segment.IndexOf('<');
                var close = segment.IndexOf('>');
                if (open < 0 || close <= open)
                {
                    continue;
                }

                var url = segment.Substring(open + 1, close - open - 1).Trim();
                var attrs = segment[(close + 1)..];

                if (!HasNextRel(attrs))
                {
                    continue;
                }

                return ToRelativeApiPath(url);
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether the attribute portion of a <c>Link</c> entry declares <c>rel="next"</c>.
    /// Tolerates quoting variants (<c>rel="next"</c>, <c>rel=next</c>) and surrounding whitespace.
    /// </summary>
    /// <param name="attributes">Text following the URL target (e.g. <c>; rel="next"</c>).</param>
    /// <returns><see langword="true"/> when a <c>next</c> relation is present.</returns>
    private static bool HasNextRel(string attributes)
    {
        foreach (var attr in attributes.Split(';'))
        {
            var trimmed = attr.Trim();
            if (!trimmed.StartsWith("rel", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var eq = trimmed.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }

            var value = trimmed[(eq + 1)..].Trim().Trim('"').Trim();
            // rel can be space-separated list of relation types per RFC 5988.
            foreach (var rel in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(rel, "next", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Reduces an absolute Tracker API URL to a path relative to the <c>/v3/</c> base address,
    /// matching the relative-path convention used by the rest of this client. Falls back to the
    /// original string when no <c>/v3/</c> segment is found.
    /// </summary>
    /// <param name="url">Absolute or relative URL from a <c>Link</c> header.</param>
    /// <returns>Path + query relative to the base address (without leading slash).</returns>
    private static string ToRelativeApiPath(string url)
    {
        const string marker = "/v3/";
        var idx = url.IndexOf(marker, StringComparison.Ordinal);
        if (idx >= 0)
        {
            return url[(idx + marker.Length)..];
        }

        // No /v3/ prefix: treat as already-relative, just strip any leading slash.
        return url.TrimStart('/');
    }

    private static async Task<JsonElement> ReadJsonOrThrow(HttpResponseMessage resp, CancellationToken ct)
    {
        if (!resp.IsSuccessStatusCode)
        {
            var status = (int)resp.StatusCode;
            var code = status switch
            {
                401 => ErrorCode.AuthFailed,
                403 => ErrorCode.Forbidden,
                404 => ErrorCode.NotFound,
                429 => ErrorCode.RateLimited,
                >= 500 and < 600 => ErrorCode.ServerError,
                _ => ErrorCode.Unexpected,
            };
            var body = await resp.Content.ReadAsStringAsync(ct);
            var trace = resp.Headers.TryGetValues("X-Trace-Id", out var v) ? v.FirstOrDefault() : null;
            throw new TrackerException(
                code,
                $"Tracker API returned HTTP {status}.{(string.IsNullOrWhiteSpace(body) ? string.Empty : " " + body)}",
                httpStatus: status,
                traceId: trace);
        }

        if (resp.Content.Headers.ContentLength == 0)
        {
            return default;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }
}
