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
    /// Persists <paramref name="refreshToken"/> as the profile's current refresh token.
    /// </summary>
    /// <param name="refreshToken">The freshly issued (rotated) refresh token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the token has been written.</returns>
    /// <remarks>
    /// Called on the request hot path, only when the value actually changed. Implementations
    /// may throw: the caller treats a failed save as non-fatal (the access token in hand is
    /// still valid) and reports it as a warning.
    /// </remarks>
    Task SaveRefreshToken(string refreshToken, CancellationToken ct);
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
    public FederatedTokenProvider(
        string cacheKey,
        ECDsa key,
        TokenCache cache,
        IFederatedRefreshClient refresh,
        string refreshToken,
        string clientId,
        IFederatedReloginHandler? relogin = null,
        string? reloginCommandHint = null,
        IRefreshTokenSink? refreshTokenSink = null)
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

                // The federation may rotate the refresh token on every exchange: the one we
                // just sent is then already dead server-side, and the replacement exists only
                // in this response. Adopt it in memory and push it to disk.
                await AdoptRotatedRefreshToken(result.RefreshToken, ct);
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
    /// the last write survives on disk. The loser's token is then the live one for nobody and
    /// the profile is left holding a token that has already been superseded, so the next run
    /// falls back to re-login. The config lock cannot fix this: the race is on the server, which
    /// invalidated the shared predecessor before either process reached the file. Serializing it
    /// would require holding the lock across the whole network exchange — every <c>yt</c>
    /// blocking on every other one's token refresh — which is a worse trade than an occasional
    /// re-login.
    /// </para>
    /// </remarks>
    private async Task AdoptRotatedRefreshToken(string? newToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(newToken) || string.Equals(newToken, _refreshToken, StringComparison.Ordinal))
        {
            return;
        }

        _refreshToken = newToken;

        if (_refreshTokenSink is null)
        {
            return;
        }

        try
        {
            await _refreshTokenSink.SaveRefreshToken(newToken, ct);
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
    /// Emits a structured warning JSON line on <see cref="Console.Error"/> reporting that a
    /// rotated refresh token could not be persisted. Shape mirrors the error envelope
    /// (single object with a top-level <c>warning</c> key) so jq/agents can consume it.
    /// </summary>
    /// <param name="reason">Human-readable reason from the failed save.</param>
    private static void WriteRefreshTokenNotSavedWarning(string reason)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteStartObject("warning");
            w.WriteString("code", "refresh_token_not_saved");
            w.WriteString(
                "message",
                "The server issued a new refresh_token, but it could not be saved to the profile. "
                + "The current command is unaffected; a later run may require re-login "
                + "(yt auth relogin).");
            w.WriteString("reason", reason);
            w.WriteEndObject();
            w.WriteEndObject();
        }

        Console.Error.WriteLine(System.Text.Encoding.UTF8.GetString(ms.ToArray()));
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
