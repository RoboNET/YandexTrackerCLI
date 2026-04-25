using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Auth.Federated;

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Auth.Federated;
using Http;
using Core.Api.Errors;
using YandexTrackerCLI.Core.Http;
using YandexTrackerCLI.Interactive;

/// <summary>
/// Юнит-тесты <see cref="FederatedOAuthFlow"/>: проверяют DPoP-биндинг (jkt в authorize URL,
/// DPoP header на token-обмене), nonce-retry, корректную обработку отсутствующего refresh_token.
/// </summary>
public sealed class FederatedOAuthFlowTests
{
    private const string DefaultTokenEndpoint = "https://token.example.test/oauth/token";
    private const string DefaultAuthorizeEndpoint = "https://auth.example.test/oauth/authorize";

    [Test]
    public async Task RunAsync_AddsDpopJktQueryParameter_ToAuthorizeUrl()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var expectedJkt = DPoPProof.ComputeJktThumbprint(key);

        var browser = new RecordingBrowserLauncher();
        var handler = new TestHttpMessageHandler().Push(_ => MakeTokenResponse(
            access: "iam-1",
            refresh: "rt-1"));
        using var http = new HttpClient(handler);

        // Drive the flow: BrowserLauncher hits the callback URL the moment the flow opens it.
        var task = FederatedOAuthFlow.RunAsync(
            federationId: "fed-1",
            clientId: "yc.oauth.public-sdk",
            dpopKey: key,
            browser: browser,
            tokenHttp: http,
            timeout: TimeSpan.FromSeconds(5),
            ct: CancellationToken.None,
            authorizeEndpoint: DefaultAuthorizeEndpoint,
            tokenEndpoint: DefaultTokenEndpoint);

        var result = await task;

        await Assert.That(browser.OpenedUrls.Count).IsEqualTo(1);
        var url = browser.OpenedUrls[0];

        // dpop_jkt is present and matches the thumbprint of the key passed in.
        var query = ParseQuery(url);
        await Assert.That(query.ContainsKey("dpop_jkt")).IsTrue();
        await Assert.That(query["dpop_jkt"]).IsEqualTo(expectedJkt);

        // Sanity: standard PKCE+federated parameters are still there.
        await Assert.That(query["response_type"]).IsEqualTo("code");
        await Assert.That(query["client_id"]).IsEqualTo("yc.oauth.public-sdk");
        await Assert.That(query["yc_federation_hint"]).IsEqualTo("fed-1");
        await Assert.That(query["code_challenge_method"]).IsEqualTo("S256");

        await Assert.That(result.AccessToken).IsEqualTo("iam-1");
        await Assert.That(result.RefreshToken).IsEqualTo("rt-1");
    }

    [Test]
    public async Task RunAsync_AddsDPoPHeader_ToTokenExchangePost()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var browser = new RecordingBrowserLauncher();
        var handler = new TestHttpMessageHandler().Push(_ => MakeTokenResponse(
            access: "iam-1",
            refresh: "rt-1"));
        using var http = new HttpClient(handler);

        var result = await FederatedOAuthFlow.RunAsync(
            federationId: "fed-1",
            clientId: "yc.oauth.public-sdk",
            dpopKey: key,
            browser: browser,
            tokenHttp: http,
            timeout: TimeSpan.FromSeconds(5),
            ct: CancellationToken.None,
            authorizeEndpoint: DefaultAuthorizeEndpoint,
            tokenEndpoint: DefaultTokenEndpoint);

        await Assert.That(handler.Seen.Count).IsEqualTo(1);
        var seen = handler.Seen[0];

        await Assert.That(seen.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(seen.RequestUri!.ToString()).IsEqualTo(DefaultTokenEndpoint);
        await Assert.That(seen.Headers.Contains("DPoP")).IsTrue();

        var proof = seen.Headers.GetValues("DPoP").Single();
        var segments = proof.Split('.');
        await Assert.That(segments.Length).IsEqualTo(3);

        var headerJson = Encoding.UTF8.GetString(DecodeBase64Url(segments[0]));
        using (var hdoc = JsonDocument.Parse(headerJson))
        {
            await Assert.That(hdoc.RootElement.GetProperty("alg").GetString()).IsEqualTo("ES256");
            await Assert.That(hdoc.RootElement.GetProperty("typ").GetString()).IsEqualTo("dpop+jwt");
        }

        var payloadJson = Encoding.UTF8.GetString(DecodeBase64Url(segments[1]));
        using (var pdoc = JsonDocument.Parse(payloadJson))
        {
            await Assert.That(pdoc.RootElement.GetProperty("htm").GetString()).IsEqualTo("POST");
            await Assert.That(pdoc.RootElement.GetProperty("htu").GetString()).IsEqualTo(DefaultTokenEndpoint);
            await Assert.That(pdoc.RootElement.TryGetProperty("iat", out _)).IsTrue();
            await Assert.That(pdoc.RootElement.TryGetProperty("jti", out _)).IsTrue();
            // No nonce on the first attempt.
            await Assert.That(pdoc.RootElement.TryGetProperty("nonce", out _)).IsFalse();
        }

        await Assert.That(result.AccessToken).IsEqualTo("iam-1");
    }

    [Test]
    public async Task RunAsync_OnNonceChallenge_RetriesOnce_WithNonceClaim()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var handler = new TestHttpMessageHandler();
        handler.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"use_dpop_nonce\"}", Encoding.UTF8, "application/json"),
            };
            r.Headers.TryAddWithoutValidation("DPoP-Nonce", "server-nonce-xyz");
            return r;
        });
        handler.Push(_ => MakeTokenResponse(access: "iam-after-nonce", refresh: "rt-bound"));

        using var http = new HttpClient(handler);
        var browser = new RecordingBrowserLauncher();

        var result = await FederatedOAuthFlow.RunAsync(
            federationId: "fed-1",
            clientId: "yc.oauth.public-sdk",
            dpopKey: key,
            browser: browser,
            tokenHttp: http,
            timeout: TimeSpan.FromSeconds(5),
            ct: CancellationToken.None,
            authorizeEndpoint: DefaultAuthorizeEndpoint,
            tokenEndpoint: DefaultTokenEndpoint);

        await Assert.That(handler.Seen.Count).IsEqualTo(2);

        // First attempt: DPoP proof has no nonce claim.
        var firstProof = handler.Seen[0].Headers.GetValues("DPoP").Single();
        var firstPayload = Encoding.UTF8.GetString(DecodeBase64Url(firstProof.Split('.')[1]));
        using (var d1 = JsonDocument.Parse(firstPayload))
        {
            await Assert.That(d1.RootElement.TryGetProperty("nonce", out _)).IsFalse();
        }

        // Second attempt: nonce echoed back.
        var secondProof = handler.Seen[1].Headers.GetValues("DPoP").Single();
        var secondPayload = Encoding.UTF8.GetString(DecodeBase64Url(secondProof.Split('.')[1]));
        using (var d2 = JsonDocument.Parse(secondPayload))
        {
            await Assert.That(d2.RootElement.GetProperty("nonce").GetString()).IsEqualTo("server-nonce-xyz");
        }

        await Assert.That(result.AccessToken).IsEqualTo("iam-after-nonce");
        await Assert.That(result.RefreshToken).IsEqualTo("rt-bound");
    }

    [Test]
    public async Task RunAsync_OnNonceChallenge_TwoBadResponses_DoesNotRetryAgain()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var handler = new TestHttpMessageHandler();
        handler.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("first-fail"),
            };
            r.Headers.TryAddWithoutValidation("DPoP-Nonce", "n1");
            return r;
        });
        handler.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("second-fail"),
            };
            r.Headers.TryAddWithoutValidation("DPoP-Nonce", "n2");
            return r;
        });

        using var http = new HttpClient(handler);
        var browser = new RecordingBrowserLauncher();

        var ex = await Assert.ThrowsAsync<TrackerException>(() => FederatedOAuthFlow.RunAsync(
            federationId: "fed-1",
            clientId: "yc.oauth.public-sdk",
            dpopKey: key,
            browser: browser,
            tokenHttp: http,
            timeout: TimeSpan.FromSeconds(5),
            ct: CancellationToken.None,
            authorizeEndpoint: DefaultAuthorizeEndpoint,
            tokenEndpoint: DefaultTokenEndpoint));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.AuthFailed);
        await Assert.That(ex.HttpStatus).IsEqualTo(400);
        // Exactly two attempts: original + one retry, never a third.
        await Assert.That(handler.Seen.Count).IsEqualTo(2);
    }

    [Test]
    public async Task RunAsync_RefreshTokenInResponse_IsPropagated()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var browser = new RecordingBrowserLauncher();
        var handler = new TestHttpMessageHandler().Push(_ => MakeTokenResponse(
            access: "t1.access",
            refresh: "rt.bound.to.dpop"));
        using var http = new HttpClient(handler);

        var result = await FederatedOAuthFlow.RunAsync(
            federationId: "fed-1",
            clientId: "yc.oauth.public-sdk",
            dpopKey: key,
            browser: browser,
            tokenHttp: http,
            timeout: TimeSpan.FromSeconds(5),
            ct: CancellationToken.None,
            authorizeEndpoint: DefaultAuthorizeEndpoint,
            tokenEndpoint: DefaultTokenEndpoint);

        await Assert.That(result.RefreshToken).IsEqualTo("rt.bound.to.dpop");
    }

    [Test]
    public async Task RunAsync_RefreshTokenMissing_DoesNotThrow_ReturnsNullRefresh()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var browser = new RecordingBrowserLauncher();

        // Server returns 200 with access_token only — no refresh_token field.
        var handler = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"t1.access","scope":"openid","token_type":"Bearer","expires_in":43199}""",
                Encoding.UTF8,
                "application/json"),
        });
        using var http = new HttpClient(handler);

        var result = await FederatedOAuthFlow.RunAsync(
            federationId: "fed-1",
            clientId: "yc.oauth.public-sdk",
            dpopKey: key,
            browser: browser,
            tokenHttp: http,
            timeout: TimeSpan.FromSeconds(5),
            ct: CancellationToken.None,
            authorizeEndpoint: DefaultAuthorizeEndpoint,
            tokenEndpoint: DefaultTokenEndpoint);

        await Assert.That(result.AccessToken).IsEqualTo("t1.access");
        await Assert.That(result.RefreshToken).IsNull();
    }

    [Test]
    public async Task WireLogSink_ReceivesAuthorizeUrl()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var expectedJkt = DPoPProof.ComputeJktThumbprint(key);

        var sink = new InMemorySink();
        var browser = new RecordingBrowserLauncher();
        var handler = new TestHttpMessageHandler().Push(_ => MakeTokenResponse(
            access: "iam-1",
            refresh: "rt-1"));
        using var http = new HttpClient(handler);

        var result = await FederatedOAuthFlow.RunAsync(
            federationId: "fed-1",
            clientId: "yc.oauth.public-sdk",
            dpopKey: key,
            browser: browser,
            tokenHttp: http,
            timeout: TimeSpan.FromSeconds(5),
            ct: CancellationToken.None,
            authorizeEndpoint: DefaultAuthorizeEndpoint,
            tokenEndpoint: DefaultTokenEndpoint,
            wireLogSink: sink);

        await Assert.That(result.AccessToken).IsEqualTo("iam-1");
        // Exactly one record was written by the flow itself (authorize-url block);
        // any token-exchange records would go through the supplied HttpClient, which
        // here uses a TestHttpMessageHandler — not wrapped with WireLogHandler.
        await Assert.That(sink.Records.Count).IsEqualTo(1);
        var record = sink.Records[0];

        // Marker line.
        await Assert.That(record).Contains("authorize-url");
        // Tilde-prefixed pseudo-request line with the exact authorize URL the browser saw.
        var browsedUrl = browser.OpenedUrls.Single();
        await Assert.That(record).Contains("~ GET " + browsedUrl);
        // The dpop_jkt value embedded in the URL matches the key thumbprint.
        await Assert.That(record).Contains("dpop_jkt=" + Uri.EscapeDataString(expectedJkt));
        await Assert.That(record).Contains("yc_federation_hint=fed-1");
        // No mask placeholder appears — the URL is logged verbatim regardless of mask mode.
        await Assert.That(record).DoesNotContain("***");
    }

    /// <summary>
    /// OIDC convention: the <c>offline_access</c> scope signals the authorization
    /// server to issue a refresh_token alongside the access_token. yc requests it
    /// (verified by mitmproxy trace) and we must do the same to obtain a refresh
    /// token — otherwise the user is forced to re-authenticate every 12 hours.
    /// </summary>
    [Test]
    public async Task Build_AuthorizeUrl_IncludesOfflineAccessScope()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var browser = new RecordingBrowserLauncher();
        var handler = new TestHttpMessageHandler().Push(_ => MakeTokenResponse(
            access: "iam-1",
            refresh: "rt-1"));
        using var http = new HttpClient(handler);

        await FederatedOAuthFlow.RunAsync(
            federationId: "fed-1",
            clientId: "yc.oauth.public-sdk",
            dpopKey: key,
            browser: browser,
            tokenHttp: http,
            timeout: TimeSpan.FromSeconds(5),
            ct: CancellationToken.None,
            authorizeEndpoint: DefaultAuthorizeEndpoint,
            tokenEndpoint: DefaultTokenEndpoint);

        var url = browser.OpenedUrls.Single();

        // Either form of URL-encoded space is valid (RFC 3986 allows either '+' or '%20'
        // in the query component). Uri.EscapeDataString emits %20, so accept both.
        var hasPlusForm = url.Contains("scope=openid+offline_access", StringComparison.Ordinal);
        var hasPercentForm = url.Contains("scope=openid%20offline_access", StringComparison.Ordinal);
        await Assert.That(hasPlusForm || hasPercentForm).IsTrue();

        // After parsing, the unescaped scope value must be exactly the two scopes
        // separated by a single space.
        var query = ParseQuery(url);
        await Assert.That(query["scope"]).IsEqualTo("openid offline_access");
    }

    [Test]
    public async Task RunAsync_NullKey_Throws_ArgumentNullException()
    {
        var browser = new RecordingBrowserLauncher();
        using var http = new HttpClient(new TestHttpMessageHandler());

        await Assert.ThrowsAsync<ArgumentNullException>(() => FederatedOAuthFlow.RunAsync(
            federationId: "fed-1",
            clientId: "yc.oauth.public-sdk",
            dpopKey: null!,
            browser: browser,
            tokenHttp: http,
            timeout: TimeSpan.FromSeconds(5),
            ct: CancellationToken.None,
            authorizeEndpoint: DefaultAuthorizeEndpoint,
            tokenEndpoint: DefaultTokenEndpoint));
    }

    private static HttpResponseMessage MakeTokenResponse(string access, string? refresh)
    {
        var body = refresh is null
            ? $"{{\"access_token\":\"{access}\",\"token_type\":\"Bearer\",\"expires_in\":3600}}"
            : $"{{\"access_token\":\"{access}\",\"refresh_token\":\"{refresh}\",\"token_type\":\"Bearer\",\"expires_in\":3600}}";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static Dictionary<string, string> ParseQuery(string url)
    {
        var qIdx = url.IndexOf('?');
        var query = qIdx < 0 ? string.Empty : url[(qIdx + 1)..];
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                dict[Uri.UnescapeDataString(pair)] = string.Empty;
                continue;
            }

            var k = Uri.UnescapeDataString(pair[..eq]);
            var v = Uri.UnescapeDataString(pair[(eq + 1)..]);
            dict[k] = v;
        }

        return dict;
    }

    private static byte[] DecodeBase64Url(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }

    /// <summary>
    /// Minimal in-memory <see cref="IWireLogSink"/> for assertions: stores every
    /// <see cref="WriteAsync"/> payload as a separate record and exposes them via
    /// <see cref="Records"/>. Thread-safe via <see cref="Lock"/>.
    /// </summary>
    private sealed class InMemorySink : IWireLogSink
    {
        private readonly Lock _gate = new();
        private readonly List<string> _records = new();

        public IReadOnlyList<string> Records
        {
            get
            {
                lock (_gate)
                {
                    return _records.ToArray();
                }
            }
        }

        public ValueTask WriteAsync(string text, CancellationToken ct)
        {
            lock (_gate)
            {
                _records.Add(text);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Records every URL passed to <see cref="OpenAsync"/> and immediately performs
    /// an HTTP GET against the loopback callback so the surrounding
    /// <see cref="LocalCallbackServer"/> proceeds without a real browser.
    /// </summary>
    private sealed class RecordingBrowserLauncher : IBrowserLauncher
    {
        public List<string> OpenedUrls { get; } = new();

        public async Task OpenAsync(string url, CancellationToken ct)
        {
            OpenedUrls.Add(url);

            // Extract redirect_uri and state from the authorize URL, then complete the
            // callback so the server can release.
            var query = ParseQuery(url);
            var redirectUri = query["redirect_uri"];
            var state = query["state"];

            // Run on a background task so RunAsync can already be awaiting the listener
            // by the time we POST back. Use a fresh HttpClient to avoid sharing state.
            _ = Task.Run(async () =>
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    var callback = $"{redirectUri}?code=mocked-auth-code&state={Uri.EscapeDataString(state)}";
                    using var resp = await http.GetAsync(callback, ct);
                }
                catch
                {
                    // Best-effort: if the listener already closed, swallow.
                }
            }, ct);

            await Task.CompletedTask;
        }
    }
}
