using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Auth.Federated;

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using TUnit.Core;
using YandexTrackerCLI.Auth.Federated;
using YandexTrackerCLI.Interactive;
using Http;

/// <summary>
/// Verifies that <see cref="FederatedOAuthFlow.RunAsync"/> emits phase events in the
/// canonical order so the interactive UI (Spectre status) can render meaningful
/// transitions instead of one opaque "doing federated login" label.
/// </summary>
public sealed class FederatedOAuthFlowPhaseTests
{
    private const string DefaultTokenEndpoint = "https://token.example.test/oauth/token";
    private const string DefaultAuthorizeEndpoint = "https://auth.example.test/oauth/authorize";

    [Test]
    public async Task RunAsync_ReportsPhases_InCanonicalOrder()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var browser = new RecordingBrowserLauncher();
        var handler = new TestHttpMessageHandler().Push(_ => MakeTokenResponse("iam-1", "rt-1"));
        using var http = new HttpClient(handler);

        // Use a synchronous IProgress<T> instead of Progress<T>. Progress<T> dispatches
        // callbacks via the captured SynchronizationContext (or thread pool), which means
        // events arrive out-of-order or after the awaited RunAsync returns. For asserting
        // phase order we want fully synchronous, in-line invocation.
        var reporter = new SyncProgress<FederatedPhase>();

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
            phaseReporter: reporter);

        await Assert.That(result.AccessToken).IsEqualTo("iam-1");

        var snapshot = reporter.Events;
        await Assert.That(snapshot.Count).IsEqualTo(5);
        await Assert.That(snapshot[0].Kind).IsEqualTo(FederatedPhaseKind.StartingCallbackServer);
        await Assert.That(snapshot[1].Kind).IsEqualTo(FederatedPhaseKind.OpeningBrowser);
        await Assert.That(snapshot[2].Kind).IsEqualTo(FederatedPhaseKind.WaitingForCallback);
        await Assert.That(snapshot[3].Kind).IsEqualTo(FederatedPhaseKind.ExchangingCode);
        await Assert.That(snapshot[4].Kind).IsEqualTo(FederatedPhaseKind.Completed);

        // Callback port is set as soon as LocalCallbackServer.Start() returns and is
        // propagated through subsequent phases.
        await Assert.That(snapshot[1].CallbackPort).IsNotNull();
        await Assert.That(snapshot[2].CallbackPort).IsNotNull();
        await Assert.That(snapshot[1].CallbackPort).IsEqualTo(snapshot[2].CallbackPort);
    }

    [Test]
    public async Task RunAsync_WithoutPhaseReporter_StillSucceeds()
    {
        // Backward compat: the parameter is optional and null is the default.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var browser = new RecordingBrowserLauncher();
        var handler = new TestHttpMessageHandler().Push(_ => MakeTokenResponse("iam-1", "rt-1"));
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
            phaseReporter: null);

        await Assert.That(result.AccessToken).IsEqualTo("iam-1");
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

    /// <summary>
    /// Synchronous, in-line <see cref="IProgress{T}"/> capture: every <c>Report</c> call
    /// is appended to <see cref="Events"/> on the calling thread, with no SyncContext
    /// or thread pool indirection. Required for deterministic phase-order assertions.
    /// </summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly List<T> _events = new();
        private readonly Lock _gate = new();

        public IReadOnlyList<T> Events
        {
            get
            {
                lock (_gate)
                {
                    return _events.ToArray();
                }
            }
        }

        public void Report(T value)
        {
            lock (_gate)
            {
                _events.Add(value);
            }
        }
    }

    /// <summary>
    /// Records the URL handed to the browser and immediately fires an HTTP GET to the
    /// loopback callback so <see cref="LocalCallbackServer"/> proceeds without a real
    /// browser. Mirrors the helper in <see cref="FederatedOAuthFlowTests"/>.
    /// </summary>
    private sealed class RecordingBrowserLauncher : IBrowserLauncher
    {
        public List<string> OpenedUrls { get; } = new();

        public async Task OpenAsync(string url, CancellationToken ct)
        {
            OpenedUrls.Add(url);

            var query = ParseQuery(url);
            var redirectUri = query["redirect_uri"];
            var state = query["state"];

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
                    // Best-effort.
                }
            }, ct);

            await Task.CompletedTask;
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
    }
}
