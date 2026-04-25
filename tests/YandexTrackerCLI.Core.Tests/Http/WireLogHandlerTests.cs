namespace YandexTrackerCLI.Core.Tests.Http;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using TUnit.Core;
using YandexTrackerCLI.Core.Http;

public sealed class WireLogHandlerTests
{
    [Test]
    public async Task Logs_RequestMethodUrl_AndMaskedAuthorization()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json"),
        });
        using var http = BuildClient(sink, inner);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.tracker.yandex.net/v3/issues/_search");
        req.Headers.Authorization = new AuthenticationHeaderValue("OAuth", "y0_secret");
        req.Headers.TryAddWithoutValidation("X-Cloud-Org-ID", "bpfvd01iftssd8lp98mu");
        req.Content = new StringContent("{\"queue\":\"DEV\"}", Encoding.UTF8, "application/json");

        using var _ = await http.SendAsync(req);

        var log = sink.Joined;
        await Assert.That(log).Contains("> POST https://api.tracker.yandex.net/v3/issues/_search");
        await Assert.That(log).Contains("> Authorization: ***");
        await Assert.That(log).Contains("> X-Cloud-Org-ID: bpfvd01iftssd8lp98mu");
        await Assert.That(log).Contains("> Content-Type: application/json");
        await Assert.That(log).Contains("> {\"queue\":\"DEV\"}");
        await Assert.That(log).DoesNotContain("y0_secret");
    }

    [Test]
    public async Task Logs_ResponseStatus_HeadersAndBody()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"issues\":[]}", Encoding.UTF8, "application/json"),
            };
            return resp;
        });
        using var http = BuildClient(sink, inner);

        using var _ = await http.GetAsync("https://api.tracker.yandex.net/v3/myself");

        var log = sink.Joined;
        await Assert.That(log).Contains("< 200");
        await Assert.That(log).Contains("< Content-Type: application/json");
        await Assert.That(log).Contains("< {\"issues\":[]}");
    }

    [Test]
    public async Task Request_AndResponse_ShareSameSequenceId()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = BuildClient(sink, inner);

        using var _ = await http.GetAsync("https://api.tracker.yandex.net/v3/myself");

        var records = sink.Records;
        await Assert.That(records.Count).IsEqualTo(2);
        var reqId = ExtractTag(records[0], "req-");
        var respId = ExtractTag(records[1], "resp-");
        await Assert.That(reqId).IsEqualTo(respId);
    }

    [Test]
    public async Task Exception_Logs_ErrorLine_WithSameId()
    {
        var sink = new MemoryWireLogSink();
        var inner = new ThrowingHandler(new HttpRequestException("boom"));
        using var http = BuildClient(sink, inner);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            async () => await http.GetAsync("https://api.tracker.yandex.net/v3/myself"));
        await Assert.That(ex!.Message).IsEqualTo("boom");

        var records = sink.Records;
        await Assert.That(records.Count).IsEqualTo(2);
        var reqId = ExtractTag(records[0], "req-");
        var respId = ExtractTag(records[1], "resp-");
        await Assert.That(reqId).IsEqualTo(respId);
        await Assert.That(records[1]).Contains("! exception");
        await Assert.That(records[1]).Contains("HttpRequestException");
        await Assert.That(records[1]).Contains("boom");
    }

    [Test]
    public async Task DPoP_Header_IsMasked()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = BuildClient(sink, inner);

        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.tracker.yandex.net/v3/myself");
        req.Headers.TryAddWithoutValidation("DPoP", "ey.secret.proof");

        using var _ = await http.SendAsync(req);

        await Assert.That(sink.Joined).Contains("> DPoP: ***");
        await Assert.That(sink.Joined).DoesNotContain("ey.secret.proof");
    }

    [Test]
    public async Task Cookie_Headers_AreMasked_CaseInsensitive()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Headers.TryAddWithoutValidation("set-cookie", "session=abcdef; Path=/");
            return resp;
        });
        using var http = BuildClient(sink, inner);

        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.tracker.yandex.net/v3/myself");
        req.Headers.TryAddWithoutValidation("cookie", "session=abcdef");

        using var _ = await http.SendAsync(req);

        var log = sink.Joined;
        // Header names are normalized by HttpHeaders, but masking matches case-insensitively.
        await Assert.That(log).Contains("Cookie: ***");
        await Assert.That(log).Contains("Set-Cookie: ***");
        await Assert.That(log).DoesNotContain("session=abcdef");
    }

    [Test]
    public async Task ProxyAuthorization_Header_IsMasked()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = BuildClient(sink, inner);

        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.tracker.yandex.net/v3/myself");
        req.Headers.TryAddWithoutValidation("Proxy-Authorization", "Basic abc==");

        using var _ = await http.SendAsync(req);

        await Assert.That(sink.Joined).Contains("Proxy-Authorization: ***");
        await Assert.That(sink.Joined).DoesNotContain("Basic abc==");
    }

    [Test]
    public async Task Json_Body_MasksSensitiveFields_AndKeepsOthers()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = BuildClient(sink, inner);

        var json = """
        {"refresh_token":"r1","access_token":"a1","token":"t1","id_token":"i1","private_key":"-----BEGIN-----","password":"hunter2","code_verifier":"cv","client_secret":"cs","queue":"DEV","limit":42,"flag":true,"nullable":null}
        """;
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://example.test/x")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        using var _ = await http.SendAsync(req);

        var log = sink.Joined;
        // Sensitive fields masked.
        await Assert.That(log).Contains("\"refresh_token\":\"***\"");
        await Assert.That(log).Contains("\"access_token\":\"***\"");
        await Assert.That(log).Contains("\"token\":\"***\"");
        await Assert.That(log).Contains("\"id_token\":\"***\"");
        await Assert.That(log).Contains("\"private_key\":\"***\"");
        await Assert.That(log).Contains("\"password\":\"***\"");
        await Assert.That(log).Contains("\"code_verifier\":\"***\"");
        await Assert.That(log).Contains("\"client_secret\":\"***\"");
        // Original secret strings absent.
        await Assert.That(log).DoesNotContain("hunter2");
        await Assert.That(log).DoesNotContain("-----BEGIN-----");
        // Non-sensitive fields preserved.
        await Assert.That(log).Contains("\"queue\":\"DEV\"");
        await Assert.That(log).Contains("\"limit\":42");
        await Assert.That(log).Contains("\"flag\":true");
        await Assert.That(log).Contains("\"nullable\":null");
    }

    [Test]
    public async Task FormEncoded_Body_MasksSensitiveKeys()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = BuildClient(sink, inner);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://example.test/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = "abc123",
                ["code_verifier"] = "verysecret",
                ["refresh_token"] = "r123",
                ["client_secret"] = "topsecret",
                ["client_id"] = "yc.oauth.public-sdk",
            }),
        };

        using var _ = await http.SendAsync(req);

        var log = sink.Joined;
        await Assert.That(log).Contains("grant_type=authorization_code");
        // `code` is a short-lived authorization-code credential; mask it like other secrets.
        await Assert.That(log).Contains("code=***");
        await Assert.That(log).Contains("client_id=yc.oauth.public-sdk");
        await Assert.That(log).Contains("code_verifier=***");
        await Assert.That(log).Contains("refresh_token=***");
        await Assert.That(log).Contains("client_secret=***");
        await Assert.That(log).DoesNotContain("abc123");
        await Assert.That(log).DoesNotContain("verysecret");
        await Assert.That(log).DoesNotContain("topsecret");
        await Assert.That(log).DoesNotContain("=r123");
    }

    [Test]
    public async Task FormEncoded_Body_Masks_AuthorizationCode_AsSensitive()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = BuildClient(sink, inner);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://example.test/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = "eyJzdXBlci1zZWNyZXQtY29kZQ",
                ["redirect_uri"] = "http://127.0.0.1:50001/auth/callback",
            }),
        };

        using var _ = await http.SendAsync(req);

        var log = sink.Joined;
        await Assert.That(log).Contains("code=***");
        await Assert.That(log).DoesNotContain("eyJzdXBlci1zZWNyZXQtY29kZQ");
    }

    [Test]
    public async Task Json_Body_Masks_AuthorizationCode_AsSensitive()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = BuildClient(sink, inner);

        var json = """{"grant_type":"authorization_code","code":"eyJjb2RlMTIz","redirect_uri":"http://x"}""";
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://example.test/token")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        using var _ = await http.SendAsync(req);

        var log = sink.Joined;
        await Assert.That(log).Contains("\"code\":\"***\"");
        await Assert.That(log).DoesNotContain("eyJjb2RlMTIz");
    }

    [Test]
    public async Task Multipart_Body_LogsOnlyContentDisposition()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = BuildClient(sink, inner);

        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[] { 1, 2, 3, 4, 5 });
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "file", "secret.bin");

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://example.test/upload")
        {
            Content = multipart,
        };

        using var _ = await http.SendAsync(req);

        var log = sink.Joined;
        await Assert.That(log).Contains("<multipart>");
        // ContentDisposition rendered as "form-data; name=...; filename=..." per part.
        await Assert.That(log).Contains("part: form-data");
        await Assert.That(log).Contains("name=file");
        await Assert.That(log).Contains("secret.bin");
        // The byte-array placeholder must not appear: multipart uses its own formatter.
        await Assert.That(log).DoesNotContain("<binary,");
    }

    [Test]
    public async Task BinaryBody_RendersPlaceholder_WithByteCount()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = BuildClient(sink, inner);

        var bytes = new byte[1024];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i % 256);
        }

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://example.test/upload")
        {
            Content = content,
        };

        using var _ = await http.SendAsync(req);

        await Assert.That(sink.Joined).Contains("<binary, 1024 bytes>");
    }

    [Test]
    public async Task LargeJsonBody_Truncated_WithMarker()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = BuildClient(sink, inner);

        // Build a JSON body well beyond MaxBodyBytes.
        var sb = new StringBuilder();
        sb.Append("{\"data\":\"");
        sb.Append('A', WireLogHandler.MaxBodyBytes + 1024);
        sb.Append("\"}");
        var json = sb.ToString();

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://example.test/x")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        using var _ = await http.SendAsync(req);

        var log = sink.Joined;
        await Assert.That(log).Contains("[truncated, total");
    }

    [Test]
    public async Task NonRewindableStreamContent_NotConsumed_ForLogging()
    {
        var sink = new MemoryWireLogSink();
        var captured = new List<byte[]>();
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            // Simulate the inner transport reading the content body.
            var bytes = req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            captured.Add(bytes);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var http = BuildClient(sink, inner);

        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var stream = new MemoryStream(bytes, writable: false);
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://example.test/x")
        {
            Content = content,
        };

        using var _ = await http.SendAsync(req);

        // Inner handler must have received the original 5 bytes intact.
        await Assert.That(captured.Count).IsEqualTo(1);
        await Assert.That(captured[0].Length).IsEqualTo(5);
        await Assert.That(captured[0][0]).IsEqualTo((byte)1);
        await Assert.That(captured[0][4]).IsEqualTo((byte)5);

        // Log should reflect that we did not capture the stream body.
        await Assert.That(sink.Joined).Contains("<stream, length unknown>");
    }

    [Test]
    public async Task ConcurrentRequests_LogIdsArePaired()
    {
        var sink = new MemoryWireLogSink();
        var inner = new ConcurrentHandler();
        using var http = BuildClient(sink, inner);

        var tasks = new Task[10];
        for (var i = 0; i < tasks.Length; i++)
        {
            var idx = i;
            tasks[i] = Task.Run(async () =>
            {
                using var resp = await http.GetAsync($"https://example.test/{idx}");
            });
        }

        await Task.WhenAll(tasks);

        var records = sink.Records;
        await Assert.That(records.Count).IsEqualTo(20);

        // Each id appears exactly twice — once as req-, once as resp-.
        var reqIds = new HashSet<int>();
        var respIds = new HashSet<int>();
        foreach (var r in records)
        {
            if (r.Contains("req-", StringComparison.Ordinal))
            {
                reqIds.Add(ExtractTag(r, "req-"));
            }
            else if (r.Contains("resp-", StringComparison.Ordinal))
            {
                respIds.Add(ExtractTag(r, "resp-"));
            }
        }

        await Assert.That(reqIds.Count).IsEqualTo(10);
        await Assert.That(respIds.Count).IsEqualTo(10);
        await Assert.That(reqIds.SetEquals(respIds)).IsTrue();
    }

    [Test]
    public async Task MaskingDisabled_DoesNotMaskHeaders()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Headers.TryAddWithoutValidation("Set-Cookie", "session=abcdef; Path=/");
            return resp;
        });
        using var http = BuildClient(sink, inner, maskSensitive: false);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.tracker.yandex.net/v3/issues/_search");
        req.Headers.Authorization = new AuthenticationHeaderValue("OAuth", "y0_secret_value");
        req.Headers.TryAddWithoutValidation("DPoP", "non-jwt-value-12345");
        req.Headers.TryAddWithoutValidation("Cookie", "session=abcdef");
        req.Headers.TryAddWithoutValidation("Proxy-Authorization", "Basic abc==");

        using var _ = await http.SendAsync(req);

        var log = sink.Joined;
        await Assert.That(log).Contains("Authorization: OAuth y0_secret_value");
        await Assert.That(log).Contains("DPoP: non-jwt-value-12345");
        await Assert.That(log).Contains("Cookie: session=abcdef");
        await Assert.That(log).Contains("Set-Cookie: session=abcdef; Path=/");
        await Assert.That(log).Contains("Proxy-Authorization: Basic abc==");
        // None of these values must be replaced with the *** placeholder.
        await Assert.That(log).DoesNotContain("Authorization: ***");
        await Assert.That(log).DoesNotContain("DPoP: ***");
        await Assert.That(log).DoesNotContain("Cookie: ***");
        await Assert.That(log).DoesNotContain("Set-Cookie: ***");
        await Assert.That(log).DoesNotContain("Proxy-Authorization: ***");
    }

    [Test]
    public async Task MaskingDisabled_DoesNotMaskJsonBody()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = BuildClient(sink, inner, maskSensitive: false);

        var json = """{"token":"abc","refresh_token":"r","queue":"DEV"}""";
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://example.test/x")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        using var _ = await http.SendAsync(req);

        var log = sink.Joined;
        await Assert.That(log).Contains("\"token\":\"abc\"");
        await Assert.That(log).Contains("\"refresh_token\":\"r\"");
        await Assert.That(log).Contains("\"queue\":\"DEV\"");
        // No masked values.
        await Assert.That(log).DoesNotContain("\"token\":\"***\"");
        await Assert.That(log).DoesNotContain("\"refresh_token\":\"***\"");
    }

    [Test]
    public async Task MaskingDisabled_DoesNotMaskFormEncodedBody()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = BuildClient(sink, inner, maskSensitive: false);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://example.test/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = "raw-code-xyz",
                ["code_verifier"] = "raw-verifier",
                ["client_id"] = "yc.oauth.public-sdk",
            }),
        };

        using var _ = await http.SendAsync(req);

        var log = sink.Joined;
        await Assert.That(log).Contains("code=raw-code-xyz");
        await Assert.That(log).Contains("code_verifier=raw-verifier");
        await Assert.That(log).DoesNotContain("code=***");
        await Assert.That(log).DoesNotContain("code_verifier=***");
    }

    [Test]
    public async Task MaskingDisabled_DecodedDPoPHeader()
    {
        var sink = new MemoryWireLogSink();
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = BuildClient(sink, inner, maskSensitive: false);

        // Build a real-looking compact JWT: header / payload / signature.
        // We don't need a valid signature — only the base64url-decodable header and
        // payload JSON segments matter for ExpandJwtForLog.
        var headerJson = """{"alg":"ES256","typ":"dpop+jwt","jwk":{"kty":"EC","crv":"P-256","x":"AAA","y":"BBB"}}""";
        var payloadJson = """{"htu":"https://auth.yandex.cloud/oauth/token","htm":"POST","iat":1714024890,"jti":"abc"}""";
        var compactJwt =
            Base64Url(Encoding.UTF8.GetBytes(headerJson))
            + "."
            + Base64Url(Encoding.UTF8.GetBytes(payloadJson))
            + "."
            + Base64Url(new byte[] { 0x01, 0x02, 0x03 });

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://auth.yandex.cloud/oauth/token");
        req.Headers.TryAddWithoutValidation("DPoP", compactJwt);

        using var _ = await http.SendAsync(req);

        var log = sink.Joined;
        // 1) The JWT itself is present verbatim on the DPoP: line.
        await Assert.That(log).Contains("DPoP: " + compactJwt);
        // 2) Decoded header line uses prefix "# decoded header: <json>".
        await Assert.That(log).Contains("# decoded header: " + headerJson);
        // 3) Decoded payload line uses prefix "# decoded payload: <json>".
        await Assert.That(log).Contains("# decoded payload: " + payloadJson);
    }

    [Test]
    public async Task ExpandJwtForLog_NonJwtString_ReturnsUnchanged()
    {
        // Single token (no dots).
        await Assert.That(WireLogHandler.ExpandJwtForLog("not-a-jwt")).IsEqualTo("not-a-jwt");
        // Two segments — looks like base64url but only one dot.
        await Assert.That(WireLogHandler.ExpandJwtForLog("abc.def")).IsEqualTo("abc.def");
        // Three segments but middle is not valid JSON after decoding ("bbb" decodes
        // to bytes that don't form a JSON value).
        await Assert.That(WireLogHandler.ExpandJwtForLog("aaa.bbb.ccc")).IsEqualTo("aaa.bbb.ccc");
        // Empty string.
        await Assert.That(WireLogHandler.ExpandJwtForLog(string.Empty)).IsEqualTo(string.Empty);
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static HttpClient BuildClient(IWireLogSink sink, HttpMessageHandler inner, bool maskSensitive = true)
    {
        var wire = new WireLogHandler(sink, maskSensitive: maskSensitive) { InnerHandler = inner };
        return new HttpClient(wire, disposeHandler: false);
    }

    private static int ExtractTag(string record, string prefix)
    {
        var i = record.IndexOf(prefix, StringComparison.Ordinal);
        if (i < 0)
        {
            throw new InvalidOperationException($"prefix '{prefix}' not found in record: {record}");
        }

        var start = i + prefix.Length;
        var end = start;
        while (end < record.Length && char.IsDigit(record[end]))
        {
            end++;
        }

        return int.Parse(record[start..end], System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _ex;

        public ThrowingHandler(Exception ex) => _ex = ex;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(_ex);
    }

    private sealed class ConcurrentHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Yield to encourage concurrent interleaving.
            await Task.Yield();
            await Task.Delay(2, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
