namespace YandexTrackerCLI.Core.Http;

using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

/// <summary>
/// Delegating handler that records every outgoing HTTP request and the corresponding
/// response (or exception) into an <see cref="IWireLogSink"/> for offline debugging.
/// </summary>
/// <remarks>
/// <para>
/// The handler should be installed as the innermost <see cref="DelegatingHandler"/> of the
/// pipeline so it observes the final headers and body that are actually sent on the wire.
/// </para>
/// <para>
/// When <c>maskSensitive</c> is <c>true</c> (default), sensitive headers (<c>Authorization</c>,
/// <c>DPoP</c>, <c>Cookie</c>, <c>Set-Cookie</c>, <c>Proxy-Authorization</c>) are masked.
/// Bodies with <c>application/json</c> or <c>application/x-www-form-urlencoded</c> content-types
/// are parsed and a known set of secret-bearing fields (<c>token</c>, <c>refresh_token</c>,
/// <c>access_token</c>, <c>id_token</c>, <c>private_key</c>, <c>password</c>,
/// <c>code_verifier</c>, <c>client_secret</c>, <c>code</c>) is masked while non-sensitive
/// fields are preserved verbatim. URL query-string parameters are NOT inspected — short-lived
/// authorization codes flow through bodies, never through query strings on Yandex Cloud token
/// endpoints.
/// </para>
/// <para>
/// When <c>maskSensitive</c> is <c>false</c>, every value is logged verbatim — intended ONLY
/// for diagnosing protocol-level issues (e.g. "Dpop doesn't match credential id"). The resulting
/// file contains live access tokens, refresh tokens, OAuth codes, DPoP proofs and other
/// credentials. The operator must delete the file once the investigation is complete.
/// Additionally, in unmasked mode the value of the <c>DPoP</c> header is followed by two
/// pseudo-comment lines that show the decoded JOSE header and payload of the JWT in clear
/// text, which makes thumbprint / claim mismatches easy to spot without manual base64url
/// decoding.
/// </para>
/// <para>
/// Multipart bodies log only their <c>Content-Disposition</c> per part. Binary or unknown
/// content types are replaced with a placeholder. Bodies larger than <see cref="MaxBodyBytes"/>
/// are truncated. Non-rewindable streaming content is never read for logging purposes — the
/// upstream invocation receives the original content untouched.
/// </para>
/// </remarks>
public sealed class WireLogHandler : DelegatingHandler
{
    /// <summary>
    /// Maximum number of bytes captured from a request or response body. Bodies larger than
    /// this threshold are truncated with a marker indicating the original length.
    /// </summary>
    public const int MaxBodyBytes = 64 * 1024;

    private static readonly HashSet<string> MaskedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "DPoP",
        "Cookie",
        "Set-Cookie",
        "Proxy-Authorization",
    };

    private static readonly HashSet<string> MaskedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "token",
        "refresh_token",
        "access_token",
        "id_token",
        "private_key",
        "password",
        "code_verifier",
        "client_secret",
        "code",
    };

    private static int _sequence;

    private readonly IWireLogSink _sink;
    private readonly bool _maskSensitive;

    /// <summary>
    /// Initializes a new instance of the <see cref="WireLogHandler"/> class.
    /// </summary>
    /// <param name="sink">The destination that receives formatted wire-log blocks.</param>
    /// <param name="maskSensitive">
    /// When <c>true</c> (default), sensitive headers and JSON / form-encoded fields are
    /// replaced with <c>***</c>. When <c>false</c>, every value is logged verbatim — intended
    /// ONLY for diagnosing protocol issues; the resulting file contains live secrets.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sink"/> is <c>null</c>.</exception>
    public WireLogHandler(IWireLogSink sink, bool maskSensitive = true)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
        _maskSensitive = maskSensitive;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _sequence);
        var requestText = await FormatRequest(request, id, _maskSensitive, cancellationToken);
        await _sink.WriteAsync(requestText, cancellationToken);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            sw.Stop();
            var responseText = await FormatResponse(response, id, sw.ElapsedMilliseconds, _maskSensitive, cancellationToken);
            await _sink.WriteAsync(responseText, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var line = FormatException(ex, id, sw.ElapsedMilliseconds);
            try
            {
                await _sink.WriteAsync(line, CancellationToken.None);
            }
            catch
            {
                // Never let logging failures mask the original exception.
            }
            throw;
        }
    }

    /// <summary>
    /// Expands a compact JWT (<c>header.payload.signature</c>) into the raw string followed by
    /// two pseudo-comment lines containing the decoded JOSE header and payload. When the input
    /// is not a well-formed three-segment JWT or any segment fails to base64url-decode into
    /// valid JSON, the original string is returned unchanged.
    /// </summary>
    /// <param name="compactJwt">A compact JWT string.</param>
    /// <returns>
    /// A multi-line string. Line 1 is the original JWT; lines 2-3 are <c># decoded header: ...</c>
    /// and <c># decoded payload: ...</c> when decoding succeeds, otherwise just the original.
    /// </returns>
    /// <remarks>
    /// AOT-safe: only <see cref="JsonDocument.Parse(System.Buffers.ReadOnlySequence{byte}, JsonDocumentOptions)"/>
    /// is used to validate that each decoded segment is JSON; the decoded bytes themselves are emitted
    /// as UTF-8 text without any re-serialization.
    /// </remarks>
    public static string ExpandJwtForLog(string compactJwt)
    {
        if (string.IsNullOrEmpty(compactJwt) || !LooksLikeJwt(compactJwt))
        {
            return compactJwt;
        }

        var segments = compactJwt.Split('.');
        if (segments.Length != 3)
        {
            return compactJwt;
        }

        if (!TryDecodeJsonSegment(segments[0], out var headerJson)
            || !TryDecodeJsonSegment(segments[1], out var payloadJson))
        {
            return compactJwt;
        }

        var sb = new StringBuilder(compactJwt.Length + headerJson.Length + payloadJson.Length + 64);
        sb.Append(compactJwt);
        sb.Append('\n');
        sb.Append("# decoded header: ").Append(headerJson);
        sb.Append('\n');
        sb.Append("# decoded payload: ").Append(payloadJson);
        return sb.ToString();
    }

    private static bool LooksLikeJwt(string s)
    {
        // Three base64url segments (alphabet [A-Za-z0-9_-]) separated by exactly two dots.
        var dotCount = 0;
        var segmentLength = 0;
        foreach (var c in s)
        {
            if (c == '.')
            {
                if (segmentLength == 0)
                {
                    return false;
                }
                dotCount++;
                segmentLength = 0;
                if (dotCount > 2)
                {
                    return false;
                }
                continue;
            }

            if (!IsBase64UrlChar(c))
            {
                return false;
            }
            segmentLength++;
        }

        return dotCount == 2 && segmentLength > 0;
    }

    private static bool IsBase64UrlChar(char c) =>
        (c >= 'A' && c <= 'Z')
        || (c >= 'a' && c <= 'z')
        || (c >= '0' && c <= '9')
        || c == '_'
        || c == '-';

    private static bool TryDecodeJsonSegment(string base64UrlSegment, out string json)
    {
        json = string.Empty;
        try
        {
            var bytes = Base64UrlDecode(base64UrlSegment);
            // Validate that what we decoded is well-formed JSON.
            using (JsonDocument.Parse(bytes))
            {
                // No-op — Parse throws on malformed JSON.
            }

            json = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static byte[] Base64UrlDecode(string segment)
    {
        var padded = segment.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
            case 1:
                throw new FormatException("Invalid base64url segment length.");
        }

        return Convert.FromBase64String(padded);
    }

    private static async Task<string> FormatRequest(HttpRequestMessage req, int id, bool maskSensitive, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(Timestamp()).Append("  req-").Append(id).Append('\n');
        sb.Append("> ").Append(req.Method.Method).Append(' ');
        sb.Append(req.RequestUri?.ToString() ?? "(no uri)").Append('\n');

        WriteHeaders(sb, req.Headers, req.Content?.Headers, maskSensitive: maskSensitive);
        sb.Append(">\n");

        var body = await CaptureBody(req.Content, maskSensitive, ct);
        AppendBodyLines(sb, body, '>');
        sb.Append('\n');
        return sb.ToString();
    }

    private static async Task<string> FormatResponse(HttpResponseMessage resp, int id, long elapsedMs, bool maskSensitive, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(Timestamp()).Append("  resp-").Append(id);
        sb.Append(" (").Append(elapsedMs.ToString(CultureInfo.InvariantCulture)).Append("ms)\n");
        sb.Append("< ").Append((int)resp.StatusCode).Append(' ').Append(resp.ReasonPhrase ?? string.Empty).Append('\n');

        WriteHeaders(sb, resp.Headers, resp.Content.Headers, prefix: "< ", maskSensitive: maskSensitive);
        sb.Append("<\n");

        var body = await CaptureResponseBody(resp.Content, maskSensitive, ct);
        AppendBodyLines(sb, body, '<');
        sb.Append('\n');
        return sb.ToString();
    }

    private static string FormatException(Exception ex, int id, long elapsedMs)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(Timestamp()).Append("  resp-").Append(id);
        sb.Append(" (").Append(elapsedMs.ToString(CultureInfo.InvariantCulture)).Append("ms)\n");
        sb.Append("! exception ").Append(ex.GetType().FullName).Append(": ").Append(ex.Message).Append('\n');
        sb.Append('\n');
        return sb.ToString();
    }

    private static void WriteHeaders(
        StringBuilder sb,
        HttpHeaders general,
        HttpHeaders? content,
        string prefix = "> ",
        bool maskSensitive = true)
    {
        WriteHeaderCollection(sb, general, prefix, maskSensitive);
        if (content is not null)
        {
            WriteHeaderCollection(sb, content, prefix, maskSensitive);
        }
    }

    private static void WriteHeaderCollection(StringBuilder sb, HttpHeaders headers, string prefix, bool maskSensitive)
    {
        foreach (var header in headers)
        {
            var name = header.Key;
            var masked = maskSensitive && MaskedHeaders.Contains(name);
            var isDPoP = !maskSensitive
                && string.Equals(name, "DPoP", StringComparison.OrdinalIgnoreCase);
            foreach (var value in header.Value)
            {
                var rendered = masked ? "***" : value;
                if (isDPoP)
                {
                    rendered = ExpandJwtForLog(value);
                }

                // The expanded JWT may contain newline characters; emit each line with the
                // proper prefix so multi-line decoded blocks stay aligned with the rest of
                // the wire-log block.
                if (rendered.IndexOf('\n') < 0)
                {
                    sb.Append(prefix).Append(name).Append(": ").Append(rendered).Append('\n');
                }
                else
                {
                    var lines = rendered.Split('\n');
                    sb.Append(prefix).Append(name).Append(": ").Append(lines[0]).Append('\n');
                    for (var i = 1; i < lines.Length; i++)
                    {
                        sb.Append(prefix).Append(lines[i]).Append('\n');
                    }
                }
            }
        }
    }

    private static void AppendBodyLines(StringBuilder sb, string body, char prefixChar)
    {
        if (body.Length == 0)
        {
            return;
        }

        // Normalize CRLF/LF to single newline-split entries; preserve content of each line.
        var lines = body.Split('\n');
        var lastIndex = lines.Length - 1;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            // Drop trailing CR if any (we split on LF only).
            if (line.Length > 0 && line[^1] == '\r')
            {
                line = line[..^1];
            }

            // Skip a final empty line caused by a trailing newline in the source string.
            if (i == lastIndex && line.Length == 0)
            {
                continue;
            }

            sb.Append(prefixChar).Append(' ').Append(line).Append('\n');
        }
    }

    private static async Task<string> CaptureBody(HttpContent? content, bool maskSensitive, CancellationToken ct)
    {
        if (content is null)
        {
            return string.Empty;
        }

        // Multipart: log only Content-Disposition per part (and Content-Length when known).
        if (content is MultipartContent multipart)
        {
            return FormatMultipart(multipart);
        }

        // Only capture content types that we know are buffered/rewindable, so we never
        // disturb upstream streaming or single-shot content.
        if (!IsRewindableContent(content))
        {
            return "<stream, length unknown>";
        }

        var contentType = content.Headers.ContentType?.MediaType ?? string.Empty;
        var bytes = await content.ReadAsByteArrayAsync(ct);

        return FormatBytes(bytes, contentType, maskSensitive);
    }

    private static async Task<string> CaptureResponseBody(HttpContent? content, bool maskSensitive, CancellationToken ct)
    {
        if (content is null)
        {
            return string.Empty;
        }

        // Response content is always buffered for logging (the body has already arrived).
        var contentType = content.Headers.ContentType?.MediaType ?? string.Empty;
        var bytes = await content.ReadAsByteArrayAsync(ct);
        return FormatBytes(bytes, contentType, maskSensitive);
    }

    private static string FormatMultipart(MultipartContent multipart)
    {
        var sb = new StringBuilder();
        sb.Append("<multipart>");
        foreach (var part in multipart)
        {
            sb.Append('\n');
            var disposition = part.Headers.ContentDisposition?.ToString() ?? "(no Content-Disposition)";
            sb.Append("part: ").Append(disposition);
            if (part.Headers.ContentLength is { } len)
            {
                sb.Append(" (").Append(len).Append(" bytes)");
            }
        }

        return sb.ToString();
    }

    private static bool IsRewindableContent(HttpContent content) =>
        content is StringContent
            or ByteArrayContent
            or FormUrlEncodedContent
            // JsonContent inherits from HttpContent (not ByteArrayContent), but is
            // safely buffered; detect by full type name to avoid taking a hard
            // dependency on the type here.
            || content.GetType().FullName == "System.Net.Http.Json.JsonContent";

    private static string FormatBytes(byte[] bytes, string contentType, bool maskSensitive)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var truncated = bytes.Length > MaxBodyBytes;
        var slice = truncated ? bytes.AsSpan(0, MaxBodyBytes).ToArray() : bytes;

        var ct = contentType.ToLowerInvariant();
        string body;
        if (ct == "application/json" || ct.EndsWith("+json", StringComparison.Ordinal))
        {
            body = maskSensitive ? MaskJson(slice) : Encoding.UTF8.GetString(slice);
        }
        else if (ct == "application/x-www-form-urlencoded")
        {
            body = maskSensitive ? MaskFormUrlEncoded(slice) : Encoding.UTF8.GetString(slice);
        }
        else if (IsTextLike(ct))
        {
            body = Encoding.UTF8.GetString(slice);
        }
        else
        {
            return $"<binary, {bytes.Length} bytes>";
        }

        if (truncated)
        {
            body += $"\n... [truncated, total {bytes.Length} bytes]";
        }

        return body;
    }

    private static bool IsTextLike(string contentType) =>
        contentType.StartsWith("text/", StringComparison.Ordinal)
        || contentType == "application/xml"
        || contentType.EndsWith("+xml", StringComparison.Ordinal);

    private static string MaskJson(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
            {
                WriteMaskedJson(doc.RootElement, writer);
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (JsonException)
        {
            // Malformed JSON — fall back to raw text so the operator can still see what was sent.
            return Encoding.UTF8.GetString(bytes);
        }
    }

    private static void WriteMaskedJson(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);
                    if (MaskedFields.Contains(prop.Name) && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        writer.WriteStringValue("***");
                    }
                    else
                    {
                        WriteMaskedJson(prop.Value, writer);
                    }
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteMaskedJson(item, writer);
                }
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string MaskFormUrlEncoded(byte[] bytes)
    {
        var raw = Encoding.UTF8.GetString(bytes);
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(raw.Length);
        var first = true;
        foreach (var pair in raw.Split('&'))
        {
            if (!first)
            {
                sb.Append('&');
            }
            first = false;

            var eq = pair.IndexOf('=');
            string key, value;
            if (eq < 0)
            {
                key = pair;
                value = string.Empty;
            }
            else
            {
                key = pair[..eq];
                value = pair[(eq + 1)..];
            }

            var decodedKey = Uri.UnescapeDataString(key);
            sb.Append(key).Append('=');
            sb.Append(MaskedFields.Contains(decodedKey) ? "***" : value);
        }

        return sb.ToString();
    }

    private static string Timestamp() =>
        DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}
