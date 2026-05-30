namespace YandexTrackerCLI.Auth.Federated;

using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Core.Api.Errors;
using YandexTrackerCLI.Core.Auth;

/// <summary>
/// Abstraction over the server-side <c>grant_type=refresh_token</c> call used to
/// renew a federated access token bound to a DPoP key.
/// </summary>
public interface IFederatedRefreshClient
{
    /// <summary>
    /// Exchanges a refresh token (bound to <paramref name="key"/>) for a fresh access token.
    /// </summary>
    /// <param name="refreshToken">Current refresh token.</param>
    /// <param name="clientId">OAuth public client id (e.g. <c>yc.oauth.public-sdk</c>).</param>
    /// <param name="key">ECDSA key used to sign the DPoP proof on the refresh request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>New <see cref="FederatedTokenResult"/>.</returns>
    Task<FederatedTokenResult> Refresh(
        string refreshToken,
        string clientId,
        ECDsa key,
        CancellationToken ct);
}

/// <summary>
/// Default <see cref="IFederatedRefreshClient"/>: POSTs to the federated token endpoint
/// with a DPoP proof, and handles the one-shot <c>401 DPoP-Nonce</c> challenge by
/// retrying with the server-provided nonce included in the proof.
/// </summary>
public sealed class FederatedRefreshClient : IFederatedRefreshClient
{
    private readonly HttpClient _http;
    private readonly string _endpoint;

    /// <summary>
    /// Initializes a new <see cref="FederatedRefreshClient"/>.
    /// </summary>
    /// <param name="http">HTTP client used to talk to the token endpoint.</param>
    /// <param name="endpoint">Optional override for the token endpoint URL.</param>
    public FederatedRefreshClient(HttpClient http, string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _endpoint = endpoint ?? FederatedOAuthFlow.DefaultTokenEndpoint;
    }

    /// <inheritdoc />
    public async Task<FederatedTokenResult> Refresh(
        string refreshToken,
        string clientId,
        ECDsa key,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(key);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        };

        // First attempt: no nonce.
        using (var req = new HttpRequestMessage(HttpMethod.Post, _endpoint))
        {
            req.Content = new FormUrlEncodedContent(form);
            req.Headers.TryAddWithoutValidation("DPoP", DPoPProof.Build(key, "POST", _endpoint));
            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (resp.IsSuccessStatusCode)
            {
                return ParseResult(body);
            }

            // RFC 9449: server may challenge with 401 + DPoP-Nonce; we must retry once with that nonce.
            if ((int)resp.StatusCode == 401
                && resp.Headers.TryGetValues("DPoP-Nonce", out var nonces))
            {
                var nonce = nonces.FirstOrDefault();
                if (!string.IsNullOrEmpty(nonce))
                {
                    return await RefreshWithNonce(form, key, nonce, ct);
                }
            }

            throw new TrackerException(
                ErrorCode.AuthFailed,
                $"Refresh failed with HTTP {(int)resp.StatusCode}. {body}",
                httpStatus: (int)resp.StatusCode);
        }
    }

    private async Task<FederatedTokenResult> RefreshWithNonce(
        Dictionary<string, string> form,
        ECDsa key,
        string nonce,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        req.Content = new FormUrlEncodedContent(form);
        req.Headers.TryAddWithoutValidation("DPoP", DPoPProof.Build(key, "POST", _endpoint, nonce));
        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (resp.IsSuccessStatusCode)
        {
            return ParseResult(body);
        }

        throw new TrackerException(
            ErrorCode.AuthFailed,
            $"DPoP refresh (with nonce) failed with HTTP {(int)resp.StatusCode}. {body}",
            httpStatus: (int)resp.StatusCode);
    }

    private static FederatedTokenResult ParseResult(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("access_token", out var atEl)
            || atEl.ValueKind != JsonValueKind.String)
        {
            throw new TrackerException(ErrorCode.AuthFailed, "Refresh response missing access_token.");
        }

        var access = atEl.GetString()!;
        var refresh = doc.RootElement.TryGetProperty("refresh_token", out var rt)
            && rt.ValueKind == JsonValueKind.String
                ? rt.GetString()
                : null;
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei)
            && ei.ValueKind == JsonValueKind.Number
                ? ei.GetInt64()
                : 3600;
        return new FederatedTokenResult(access, refresh, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }
}

/// <summary>
/// Handler that re-runs the federated browser login flow inline (interactive TTY only)
/// when a token refresh fails because the refresh token is expired/revoked or the bound
/// key no longer matches. Implementations open the system browser, complete the PKCE+DPoP
/// flow, persist the new tokens, and return the fresh <see cref="FederatedTokenResult"/>.
/// </summary>
public interface IFederatedReloginHandler
{
    /// <summary>
    /// Re-authenticates via the browser and returns freshly issued tokens.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new <see cref="FederatedTokenResult"/>.</returns>
    Task<FederatedTokenResult> ReloginAsync(CancellationToken ct);
}

/// <summary>
/// <see cref="IAuthProvider"/> for federated user login with DPoP-bound tokens:
/// serves cached access tokens, and on cache miss signs a DPoP proof and refreshes
/// against the federated token endpoint. Also publishes a DPoP proof factory on
/// <see cref="Core.Http.DPoPHandler.ProofFactory"/> so that the outgoing HTTP pipeline
/// attaches a <c>DPoP:</c> header bound to each API request.
/// </summary>
public sealed class FederatedTokenProvider : IAuthProvider, IDisposable
{
    private readonly string _cacheKey;
    private readonly ECDsa _key;
    private readonly TokenCache _cache;
    private readonly IFederatedRefreshClient _refresh;
    private readonly string _clientId;
    private readonly IFederatedReloginHandler? _relogin;
    private readonly string? _reloginCommandHint;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Mutable refresh token: re-login (interactive auto-recovery) hands us a new one, and a
    // later refresh in the same process must use it. Only ever read/written under `_gate`.
    private string _refreshToken;
    private bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="FederatedTokenProvider"/>.
    /// </summary>
    /// <param name="cacheKey">Stable key identifying this credential in the token cache.</param>
    /// <param name="key">ECDSA P-256 private key bound to the tokens. Not disposed by this provider.</param>
    /// <param name="cache">Shared <see cref="TokenCache"/>.</param>
    /// <param name="refresh">Refresh client used on cache misses.</param>
    /// <param name="refreshToken">The refresh token (persistent).</param>
    /// <param name="clientId">OAuth public client id.</param>
    /// <param name="relogin">
    /// Optional inline re-login handler. When supplied (interactive TTY), a refresh that fails
    /// with <see cref="ErrorCode.AuthFailed"/> triggers the browser re-login flow instead of
    /// surfacing the failure; the freshly issued access token is then served.
    /// </param>
    /// <param name="reloginCommandHint">
    /// Optional command hint (e.g. <c>yt auth relogin --profile foo</c>) embedded in the
    /// <see cref="ErrorCode.AuthFailed"/> error when no <paramref name="relogin"/> handler is
    /// available (non-interactive), so agents/CI know exactly how to recover.
    /// </param>
    public FederatedTokenProvider(
        string cacheKey,
        ECDsa key,
        TokenCache cache,
        IFederatedRefreshClient refresh,
        string refreshToken,
        string clientId,
        IFederatedReloginHandler? relogin = null,
        string? reloginCommandHint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        _cacheKey = cacheKey;
        _key = key;
        _cache = cache;
        _refresh = refresh;
        _refreshToken = refreshToken;
        _clientId = clientId;
        _relogin = relogin;
        _reloginCommandHint = reloginCommandHint;
    }

    /// <inheritdoc />
    public async Task<AuthenticationHeaderValue> GetAuthorizationAsync(CancellationToken ct)
    {
        // Publish the proof factory for the duration of this async flow so that the
        // DPoPHandler downstream attaches a `DPoP:` header bound to each API request.
        Core.Http.DPoPHandler.ProofFactory.Value =
            (method, url) => DPoPProof.Build(_key, method, url);

        var cached = await _cache.GetAsync(_cacheKey, ct: ct);
        if (cached is not null)
        {
            return new AuthenticationHeaderValue("Bearer", cached.Token);
        }

        await _gate.WaitAsync(ct);
        try
        {
            cached = await _cache.GetAsync(_cacheKey, ct: ct);
            if (cached is not null)
            {
                return new AuthenticationHeaderValue("Bearer", cached.Token);
            }

            FederatedTokenResult result;
            try
            {
                result = await _refresh.Refresh(_refreshToken, _clientId, _key, ct);
            }
            catch (TrackerException ex) when (ex.Code == ErrorCode.AuthFailed)
            {
                if (_relogin is null)
                {
                    // Non-interactive: do NOT open a browser. Surface an actionable error that
                    // names the exact recovery command (also exposed as `relogin_command`).
                    var hint = _reloginCommandHint ?? "yt auth relogin";
                    throw new TrackerException(
                        ErrorCode.AuthFailed,
                        $"{ex.Message} Re-login (opens browser): {hint}",
                        httpStatus: ex.HttpStatus,
                        inner: ex,
                        reloginCommand: hint);
                }

                // Interactive TTY: re-run the browser flow inline, adopt the new refresh token
                // for any later refresh in this process, then serve the fresh access token.
                result = await _relogin.ReloginAsync(ct);
                if (!string.IsNullOrEmpty(result.RefreshToken))
                {
                    _refreshToken = result.RefreshToken!;
                }
            }

            await _cache.SetAsync(_cacheKey, result.AccessToken, result.ExpiresAt, ct);
            return new AuthenticationHeaderValue("Bearer", result.AccessToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Releases internal synchronization primitives. Does NOT dispose the ECDSA key —
    /// lifetime ownership remains with the caller (typically <c>TrackerContext</c>).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }
}
