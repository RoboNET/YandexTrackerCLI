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
/// Narrow persistence hook for a refresh token that the server rotated during a
/// <c>grant_type=refresh_token</c> exchange. Implemented in the composition root
/// (<c>TrackerContextFactory</c>), so <see cref="FederatedTokenProvider"/> stays unaware of
/// the configuration file, its layout and its locking.
/// </summary>
public interface IRefreshTokenSink
{
    /// <summary>
    /// Persists <paramref name="refreshToken"/> as the profile's current refresh token,
    /// but only if the stored token is still <paramref name="expectedCurrentToken"/>.
    /// </summary>
    /// <param name="expectedCurrentToken">
    /// The refresh token that was presented to the server for this exchange — i.e. the value
    /// the profile is expected to still hold. Implementations must compare-and-swap against
    /// it: anything else on disk means another run has moved the profile on since, and its
    /// token is the live one.
    /// </param>
    /// <param name="refreshToken">The freshly issued (rotated) refresh token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the token has been written.</returns>
    /// <remarks>
    /// Called on the request hot path, only when the value actually changed. Implementations
    /// may throw — including when the compare-and-swap fails: the caller treats a failed save
    /// as non-fatal (the access token in hand is still valid) and reports it as a warning.
    /// </remarks>
    Task SaveRefreshToken(string expectedCurrentToken, string refreshToken, CancellationToken ct);
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
    private readonly IRefreshTokenSink? _refreshTokenSink;
    private readonly TextWriter? _warningWriter;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Mutable refresh token: both a rotating federation (new token on every exchange) and
    // re-login (interactive auto-recovery) hand us a new one, and a later refresh in the same
    // process must use it. Only ever read/written under `_gate`.
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
    /// <param name="refreshTokenSink">
    /// Optional persistence hook invoked when the server rotates the refresh token during a
    /// refresh. When omitted, a rotated token is still adopted for this process but is lost
    /// on exit — the next run would start from the (now dead) token on disk.
    /// </param>
    /// <param name="warningWriter">
    /// Where the "refresh token not saved" warning goes; <c>null</c> selects
    /// <see cref="Console.Error"/>, resolved at write time. Tests inject their own writer so
    /// that a warning never lands in the process-global stderr other tests capture.
    /// </param>
    public FederatedTokenProvider(
        string cacheKey,
        ECDsa key,
        TokenCache cache,
        IFederatedRefreshClient refresh,
        string refreshToken,
        string clientId,
        IFederatedReloginHandler? relogin = null,
        string? reloginCommandHint = null,
        IRefreshTokenSink? refreshTokenSink = null,
        TextWriter? warningWriter = null)
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
        _refreshTokenSink = refreshTokenSink;
        _warningWriter = warningWriter;
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
            var fromRelogin = false;
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
                fromRelogin = true;
                if (!string.IsNullOrEmpty(result.RefreshToken))
                {
                    _refreshToken = result.RefreshToken!;
                }
            }

            // Cache the access token BEFORE persisting the rotated refresh token, and do not
            // reorder these two. The access token has already been paid for by the network
            // exchange that also burned the previous refresh token; if cancellation lands
            // between them, the cached access token still carries this process (and the next
            // one) to the end, whereas losing it would leave nothing usable anywhere: the old
            // refresh token is dead server-side and the new one is only in memory.
            await _cache.SetAsync(_cacheKey, result.AccessToken, result.ExpiresAt, ct);

            if (!fromRelogin)
            {
                // The federation may rotate the refresh token on every exchange: the one we
                // just sent is then already dead server-side, and the replacement exists only
                // in this response. Adopt it in memory and push it to disk. (After a re-login
                // the profile has already been written by FederatedReloginService.)
                await AdoptRotatedRefreshToken(result.RefreshToken, ct);
            }

            return new AuthenticationHeaderValue("Bearer", result.AccessToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Adopts a refresh token returned by the token endpoint: makes it the one used by later
    /// refreshes in this process and persists it through <see cref="IRefreshTokenSink"/>.
    /// </summary>
    /// <param name="newToken">The <c>refresh_token</c> from the response; may be <c>null</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the token has been adopted (and, if it changed, saved).</returns>
    /// <remarks>
    /// <para>
    /// Nothing is written when the server returned no token or the same one: this runs on the
    /// request hot path, and taking the config lock on every access-token renewal to rewrite an
    /// identical value would be pure cost.
    /// </para>
    /// <para>
    /// <b>Known limitation — concurrent <c>yt</c> processes.</b> Two invocations may refresh at
    /// the same time; a rotating federation issues a different refresh token to each, and only
    /// one of them can be on disk afterwards. The save is a compare-and-swap against the token
    /// this process presented, so the second writer does not clobber the first: it finds a token
    /// it did not expect, declines and warns. The user may still face a re-login — the token
    /// left on disk may not be the one the server considers live — but the profile is never left
    /// in a mixed state, and the outcome is reported instead of being silently written over. The
    /// config lock cannot fix the race itself: it happens on the server, which invalidated the
    /// shared predecessor before either process reached the file. Serializing it would require
    /// holding the lock across the whole network exchange — every <c>yt</c> blocking on every
    /// other one's token refresh — which is a worse trade than an occasional re-login.
    /// </para>
    /// </remarks>
    private async Task AdoptRotatedRefreshToken(string? newToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(newToken) || string.Equals(newToken, _refreshToken, StringComparison.Ordinal))
        {
            return;
        }

        // Read the outgoing token from the field, not from a value captured at construction:
        // this is the token just presented to the server, and on a second rotation in the same
        // process that is the previous rotation's result, not the one the process started with.
        // A stale expectation here would make every save after the first one a no-op.
        var presentedToken = _refreshToken;
        _refreshToken = newToken;

        if (_refreshTokenSink is null)
        {
            return;
        }

        try
        {
            await _refreshTokenSink.SaveRefreshToken(presentedToken, newToken, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The command itself is being cancelled — nothing to salvage, let it unwind.
            throw;
        }
        catch (Exception ex)
        {
            // Deliberate fail-soft, do NOT "fix" this into a throw: we are holding a valid
            // access token, so the command the user asked for can still complete. Failing it
            // here would turn a lost-on-disk token (cost: one re-login later) into a failed
            // command now (cost: the same re-login, plus the command). The warning is the
            // whole point — it is the only place the user learns the profile is now stale.
            WriteRefreshTokenNotSavedWarning(ex.Message);
        }
    }

    /// <summary>
    /// Emits the structured warning reporting that a rotated refresh token could not be
    /// persisted, on the injected writer or <see cref="Console.Error"/> by default.
    /// </summary>
    /// <param name="reason">Human-readable reason from the failed save.</param>
    private void WriteRefreshTokenNotSavedWarning(string reason) =>
        Output.WarningWriter.Write(
            _warningWriter ?? Console.Error,
            "refresh_token_not_saved",
            "The server issued a new refresh_token, but it could not be saved to the profile. "
            + "The current command is unaffected; a later run may require re-login "
            + "(yt auth relogin).",
            ("reason", reason));

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
