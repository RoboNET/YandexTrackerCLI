namespace YandexTrackerCLI.Auth.Federated;

using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Core.Http;
using Interactive;

/// <summary>
/// Результат успешного обмена authorization code на IAM-токен через federated OAuth flow.
/// </summary>
/// <param name="AccessToken">Short-lived IAM access token.</param>
/// <param name="RefreshToken">Refresh token (для последующего обновления, Task 25).</param>
/// <param name="ExpiresAt">UTC-момент истечения <see cref="AccessToken"/>.</param>
public sealed record FederatedTokenResult(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Дискретные фазы federated OAuth flow, эмитимые через <see cref="IProgress{T}"/>
/// для интерактивного UI (Spectre status).
/// </summary>
public enum FederatedPhaseKind
{
    /// <summary>Запуск локального loopback HTTP listener.</summary>
    StartingCallbackServer,

    /// <summary>Открытие authorize URL в системном браузере.</summary>
    OpeningBrowser,

    /// <summary>Ожидание HTTP callback от браузера.</summary>
    WaitingForCallback,

    /// <summary>POST на token endpoint (обмен code → access_token, DPoP-bound).</summary>
    ExchangingCode,

    /// <summary>Token успешно получен и распарсен.</summary>
    Completed,
}

/// <summary>
/// Событие фазы federated OAuth flow.
/// </summary>
/// <param name="Kind">Тип фазы.</param>
/// <param name="Message">Человекочитаемое сообщение (для NoopUI/wire-log).</param>
/// <param name="CallbackPort">Порт локального callback-сервера (только для <see cref="FederatedPhaseKind.StartingCallbackServer"/> и далее).</param>
public readonly record struct FederatedPhase(
    FederatedPhaseKind Kind,
    string Message,
    int? CallbackPort = null);

/// <summary>
/// Browser-based PKCE OAuth flow для federated пользовательского входа в Yandex Cloud
/// (по мотивам <c>yc init --federation-id</c>) с DPoP-биндингом авторизации.
/// </summary>
/// <remarks>
/// Шаги:
/// <list type="number">
///   <item><description>Генерация PKCE verifier + challenge (S256).</description></item>
///   <item><description>Вычисление SHA-512 JWK thumbprint (<c>jkt</c>) от публичного ECDSA-ключа,
///   который пробрасывается в authorize URL как <c>dpop_jkt</c> — это сигнализирует
///   серверу выпустить refresh_token, привязанный к этому ключу (RFC 9449 §10).
///   Yandex Cloud использует SHA-512 вместо SHA-256 из RFC 7638.</description></item>
///   <item><description>Старт локального loopback listener на <c>127.0.0.1:{port}</c>.</description></item>
///   <item><description>Сборка authorize URL с <c>yc_federation_hint</c> и <c>dpop_jkt</c>,
///   открытие через <see cref="IBrowserLauncher"/>.</description></item>
///   <item><description>Приём callback <c>/auth/callback?code=...&amp;state=...</c>, проверка <c>state</c>.</description></item>
///   <item><description>POST <c>grant_type=authorization_code</c> на token endpoint с заголовком
///   <c>DPoP: &lt;proof JWT&gt;</c>; при HTTP 400 + <c>DPoP-Nonce</c> proof пересобирается с
///   <c>nonce</c> claim и запрос повторяется ровно один раз.</description></item>
/// </list>
/// </remarks>
public static class FederatedOAuthFlow
{
    // Token endpoint inferred from authorize host; verify with full yc trace including code-exchange step.
    /// <summary>
    /// Yandex Cloud federated token endpoint.
    /// </summary>
    public const string DefaultTokenEndpoint = "https://auth.yandex.cloud/oauth/token";

    // Verified against `yc init` Charles trace 2026-04-25.
    /// <summary>
    /// Authorize endpoint Yandex Cloud (поддерживает <c>yc_federation_hint</c>).
    /// </summary>
    public const string DefaultAuthorizeEndpoint = "https://auth.yandex.cloud/oauth/authorize";

    /// <summary>
    /// Выполняет полный browser PKCE flow с DPoP-биндингом и возвращает <see cref="FederatedTokenResult"/>.
    /// </summary>
    /// <param name="federationId">ID федерации в Yandex Cloud (значение <c>yc_federation_hint</c>).</param>
    /// <param name="clientId">OAuth client id public-приложения (например, <c>yc.oauth.public-sdk</c>).</param>
    /// <param name="dpopKey">ECDSA P-256 keypair, привязываемый к выпускаемым токенам.
    /// Тот же ключ должен использоваться при последующих refresh-запросах
    /// (server привязывает JWK thumbprint к access_token и refresh_token).</param>
    /// <param name="browser">Запускатель системного браузера.</param>
    /// <param name="tokenHttp"><see cref="HttpClient"/> для обмена <c>code → token</c>.</param>
    /// <param name="timeout">Таймаут ожидания callback от браузера.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <param name="authorizeEndpoint">Override authorize URL (для тестов).</param>
    /// <param name="tokenEndpoint">Override token URL (для тестов).</param>
    /// <param name="wireLogSink">
    /// Optional sink that, when supplied, receives a one-line record describing the authorize
    /// URL just before it is opened in the user's browser. The record uses the prefix <c>~</c>
    /// (instead of <c>&gt;</c>/<c>&lt;</c>) to make clear that this is not a request issued
    /// by the CLI's <see cref="HttpClient"/> but a URL handed to the system browser. The URL
    /// is always logged verbatim because it carries no long-lived secrets: <c>code_verifier</c>
    /// and <c>client_secret</c> never appear in the authorize request, while <c>code_challenge</c>,
    /// <c>dpop_jkt</c>, and <c>state</c> are public/short-lived by design.
    /// </param>
    /// <returns>
    /// Access токен (всегда) и refresh токен (когда сервер его выдал; для DPoP-bound flow
    /// должен присутствовать, но отсутствие не считается ошибкой — пользователь работает
    /// с access на 12 часов).
    /// </returns>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.AuthFailed"/> — если callback содержит <c>error</c>, state не совпадает,
    /// отсутствует <c>code</c>, или token endpoint вернул не-2xx / ответ без <c>access_token</c>.
    /// </exception>
    public static async Task<FederatedTokenResult> RunAsync(
        string federationId,
        string clientId,
        ECDsa dpopKey,
        IBrowserLauncher browser,
        HttpClient tokenHttp,
        TimeSpan timeout,
        CancellationToken ct,
        string authorizeEndpoint = DefaultAuthorizeEndpoint,
        string tokenEndpoint = DefaultTokenEndpoint,
        IWireLogSink? wireLogSink = null,
        IProgress<FederatedPhase>? phaseReporter = null)
    {
        ArgumentNullException.ThrowIfNull(dpopKey);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(tokenHttp);
        ArgumentException.ThrowIfNullOrWhiteSpace(federationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var pkce = PkceChallengeFactory.Generate();
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var jkt = DPoPProof.ComputeJktThumbprint(dpopKey);

        phaseReporter?.Report(new FederatedPhase(
            FederatedPhaseKind.StartingCallbackServer,
            "Starting local callback server..."));

        await using var server = LocalCallbackServer.Start();
        var redirectUri = $"http://127.0.0.1:{server.Port}/auth/callback";

        // OIDC convention: `offline_access` scope signals the server to issue a refresh_token.
        // Without it, yc returns access-only and the user must re-authenticate every 12 hours.
        // Verified against a `yc init` mitmproxy trace: yc requests both scopes.
        var scope = Uri.EscapeDataString("openid offline_access");

        var authUrl =
            $"{authorizeEndpoint}?response_type=code" +
            $"&client_id={Uri.EscapeDataString(clientId)}" +
            $"&scope={scope}" +
            $"&code_challenge={Uri.EscapeDataString(pkce.Challenge)}" +
            $"&code_challenge_method=S256" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&yc_federation_hint={Uri.EscapeDataString(federationId)}" +
            $"&dpop_jkt={Uri.EscapeDataString(jkt)}" +
            $"&state={state}";

        if (wireLogSink is not null)
        {
            await wireLogSink.WriteAsync(FormatAuthorizeUrlLog(authUrl), ct);
        }

        var browserOrigin = TryGetOrigin(authUrl);
        phaseReporter?.Report(new FederatedPhase(
            FederatedPhaseKind.OpeningBrowser,
            $"Opening browser: {browserOrigin}",
            CallbackPort: server.Port));

        await browser.OpenAsync(authUrl, ct);

        phaseReporter?.Report(new FederatedPhase(
            FederatedPhaseKind.WaitingForCallback,
            $"Waiting for browser callback on :{server.Port}...",
            CallbackPort: server.Port));

        var callback = await server.AwaitCallbackAsync(timeout, ct);

        if (!string.IsNullOrEmpty(callback.Error))
        {
            throw new TrackerException(ErrorCode.AuthFailed, $"Authorization failed: {callback.Error}");
        }

        if (callback.State != state)
        {
            throw new TrackerException(ErrorCode.AuthFailed, "OAuth state mismatch (possible CSRF).");
        }

        if (string.IsNullOrEmpty(callback.Code))
        {
            throw new TrackerException(ErrorCode.AuthFailed, "OAuth callback missing code.");
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = callback.Code!,
            ["code_verifier"] = pkce.Verifier,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
        };

        phaseReporter?.Report(new FederatedPhase(
            FederatedPhaseKind.ExchangingCode,
            "Exchanging code for token (DPoP)..."));

        var (status, body, nonceChallenge) = await PostTokenRequest(
            tokenHttp, tokenEndpoint, form, dpopKey, nonce: null, ct);

        // RFC 9449 §8: server may answer 400 + DPoP-Nonce: <n>; resend with the nonce claim once.
        if (status == 400 && !string.IsNullOrEmpty(nonceChallenge))
        {
            (status, body, _) = await PostTokenRequest(
                tokenHttp, tokenEndpoint, form, dpopKey, nonce: nonceChallenge, ct);
        }

        if (status is < 200 or >= 300)
        {
            throw new TrackerException(
                ErrorCode.AuthFailed,
                $"Token exchange failed with HTTP {status}. {body}",
                httpStatus: status);
        }

        using var doc = JsonDocument.Parse(body);
        var accessToken = doc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new TrackerException(ErrorCode.AuthFailed, "Response missing access_token.");
        }

        // refresh_token is optional: with DPoP the server typically issues one, but absence
        // is degraded mode (12h access only) and not a hard error — better than failing login.
        var refreshToken = doc.RootElement.TryGetProperty("refresh_token", out var rt)
            && rt.ValueKind == JsonValueKind.String
                ? rt.GetString()
                : null;
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) && ei.ValueKind == JsonValueKind.Number
            ? ei.GetInt64()
            : 3600;

        phaseReporter?.Report(new FederatedPhase(
            FederatedPhaseKind.Completed,
            "Token exchange completed."));

        return new FederatedTokenResult(accessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    /// <summary>
    /// Извлекает scheme+host из URL для безопасной демонстрации в UI без чувствительных
    /// query-параметров. При парсинге-ошибке возвращает пустую строку.
    /// </summary>
    private static string TryGetOrigin(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return $"{uri.Scheme}://{uri.Host}";
        }

        return string.Empty;
    }

    /// <summary>
    /// Issues a single POST to the token endpoint with a freshly built DPoP proof and
    /// returns the response status, body, and optional <c>DPoP-Nonce</c> challenge value.
    /// </summary>
    private static async Task<(int Status, string Body, string? NonceChallenge)> PostTokenRequest(
        HttpClient http,
        string tokenEndpoint,
        Dictionary<string, string> form,
        ECDsa key,
        string? nonce,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);
        req.Content = new FormUrlEncodedContent(form);
        req.Headers.TryAddWithoutValidation("DPoP", DPoPProof.Build(key, "POST", tokenEndpoint, nonce));

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        string? nonceChallenge = null;
        if (resp.Headers.TryGetValues("DPoP-Nonce", out var values))
        {
            nonceChallenge = values.FirstOrDefault();
        }

        return ((int)resp.StatusCode, body, nonceChallenge);
    }

    /// <summary>
    /// Builds a wire-log block describing the authorize URL handed to the browser.
    /// The first line carries a UTC timestamp and the marker <c>authorize-url</c>; the
    /// second line uses prefix <c>~ GET</c> (rather than <c>&gt; GET</c>) so it cannot be
    /// confused with a request issued by the CLI's <see cref="HttpClient"/>.
    /// </summary>
    private static string FormatAuthorizeUrlLog(string authUrl)
    {
        var sb = new StringBuilder(authUrl.Length + 64);
        sb.Append("# ");
        sb.Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        sb.Append("  authorize-url\n");
        sb.Append("~ GET ");
        sb.Append(authUrl);
        sb.Append('\n');
        sb.Append('\n');
        return sb.ToString();
    }
}
