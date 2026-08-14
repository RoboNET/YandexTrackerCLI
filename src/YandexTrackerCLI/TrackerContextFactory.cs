namespace YandexTrackerCLI;

using System.Net.Http;
using System.Security.Cryptography;
using Auth.Federated;
using Core.Api;
using Core.Api.Errors;
using YandexTrackerCLI.Core.Auth;
using Core.Config;
using Core.Http;
using Interactive;
using Output;

/// <summary>
/// Bundle of runtime objects required to issue requests against the Yandex Tracker API:
/// a preconfigured <see cref="TrackerClient"/> facade, the resolved <see cref="EffectiveProfile"/>,
/// and the underlying <see cref="HttpClient"/> (exposed for low-level scenarios and tests).
/// </summary>
/// <remarks>
/// Disposing a <see cref="TrackerContext"/> releases the underlying <see cref="HttpClient"/>
/// (and, for service-account flows, the IAM-exchange <see cref="HttpClient"/> and <see cref="RSA"/> key).
/// </remarks>
public sealed class TrackerContext : IDisposable
{
    private readonly HttpClient? _iamHttp;
    private readonly RSA? _rsa;
    private readonly ECDsa? _ecdsa;
    private readonly IWireLogSink? _wireLogSink;
    private bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="TrackerContext"/>.
    /// </summary>
    /// <param name="client">The configured Tracker API facade.</param>
    /// <param name="profile">The effective profile used to build the HTTP pipeline.</param>
    /// <param name="rawHttp">The underlying <see cref="HttpClient"/> owned by this context.</param>
    /// <param name="iamHttp">Optional IAM-exchange <see cref="HttpClient"/> to dispose together with the context.</param>
    /// <param name="rsa">Optional RSA key to dispose together with the context (service-account flow).</param>
    /// <param name="ecdsa">Optional ECDSA key to dispose together with the context (federated DPoP flow).</param>
    /// <param name="wireLogSink">Optional wire-log sink owned by this context; flushed and closed on dispose.</param>
    /// <param name="effectiveOutputFormat">Резолвленный формат вывода (cascade: CLI → env → profile → TTY).
    /// По умолчанию <see cref="OutputFormat.Json"/>; команды, не передающие явное значение, получат сырой JSON.</param>
    /// <param name="terminalCapabilities">Резолвленные возможности терминала (color/hyperlinks/width/pager).
    /// По умолчанию — <see cref="Output.TerminalCapabilities.Disabled"/>.</param>
    public TrackerContext(
        TrackerClient client,
        EffectiveProfile profile,
        HttpClient rawHttp,
        HttpClient? iamHttp = null,
        RSA? rsa = null,
        ECDsa? ecdsa = null,
        IWireLogSink? wireLogSink = null,
        OutputFormat effectiveOutputFormat = OutputFormat.Json,
        TerminalCapabilities? terminalCapabilities = null)
    {
        Client = client;
        Profile = profile;
        RawHttp = rawHttp;
        _iamHttp = iamHttp;
        _rsa = rsa;
        _ecdsa = ecdsa;
        _wireLogSink = wireLogSink;
        EffectiveOutputFormat = effectiveOutputFormat;
        TerminalCapabilities = terminalCapabilities ?? TerminalCapabilities.Disabled;
    }

    /// <summary>
    /// Gets the Tracker API facade configured with the full handler pipeline.
    /// </summary>
    public TrackerClient Client { get; }

    /// <summary>
    /// Gets the effective profile used to build this context (org type, org id, auth, read-only policy).
    /// </summary>
    public EffectiveProfile Profile { get; }

    /// <summary>
    /// Gets the underlying <see cref="HttpClient"/>. Useful for tests and scenarios that bypass the facade.
    /// </summary>
    public HttpClient RawHttp { get; }

    /// <summary>
    /// Эффективный формат вывода после резолва cascade (CLI → env → profile → TTY-detect).
    /// Гарантированно никогда не <see cref="OutputFormat.Auto"/>.
    /// </summary>
    public OutputFormat EffectiveOutputFormat { get; }

    /// <summary>
    /// Резолвленные возможности терминала: ANSI цвета, OSC 8 hyperlinks, ширина,
    /// pager-команда. Используются rich-рендерами (detail view, comments).
    /// </summary>
    public TerminalCapabilities TerminalCapabilities { get; }

    /// <summary>
    /// Releases the underlying <see cref="HttpClient"/> and, if any, the IAM-exchange client,
    /// RSA/ECDSA keys, and the wire-log sink (in that order so the sink can flush trailing
    /// records before the file handle closes).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RawHttp.Dispose();
        _iamHttp?.Dispose();
        _rsa?.Dispose();
        _ecdsa?.Dispose();
        if (_wireLogSink is not null)
        {
            _wireLogSink.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}

/// <summary>
/// Composition root that turns the current configuration + environment + CLI flags
/// into a ready-to-use <see cref="TrackerContext"/>.
/// </summary>
public static class TrackerContextFactory
{
    /// <summary>
    /// Test-only override of the HTTP inner handler. Flows through <see cref="System.Threading.AsyncLocal{T}"/>
    /// so it stays scoped to a single test's async context. Used when the command layer calls
    /// <see cref="CreateAsync"/> without an explicit <c>innerHandler</c> (e.g., from <c>SetAction</c>).
    /// </summary>
    internal static readonly AsyncLocal<HttpMessageHandler?> TestInnerHandlerOverride = new();

    /// <summary>
    /// Test-only override of the IAM exchange client. Scoped via <see cref="System.Threading.AsyncLocal{T}"/>
    /// to a single test's async context.
    /// </summary>
    internal static readonly AsyncLocal<IIamExchangeClient?> TestIamExchangeOverride = new();

    /// <summary>
    /// Test-only override of the federated refresh client. Scoped via
    /// <see cref="System.Threading.AsyncLocal{T}"/> to a single test's async context.
    /// </summary>
    internal static readonly AsyncLocal<IFederatedRefreshClient?> TestFederatedRefreshOverride = new();

    /// <summary>
    /// Test-only override of interactivity detection for the federated auto-relogin decision.
    /// When set, it takes precedence over <see cref="ConsoleTokenReader.IsInputRedirected"/>:
    /// <c>true</c> forces interactive (inline browser relogin), <c>false</c> forces
    /// non-interactive (actionable error with <c>relogin_command</c>). Scoped via
    /// <see cref="System.Threading.AsyncLocal{T}"/> to a single test's async context.
    /// </summary>
    internal static readonly AsyncLocal<bool?> TestInteractiveOverride = new();

    /// <summary>
    /// Test-only override of the inline federated relogin handler. When set, it is injected
    /// into the <see cref="FederatedTokenProvider"/> in place of the production handler.
    /// Scoped via <see cref="System.Threading.AsyncLocal{T}"/> to a single test's async context.
    /// </summary>
    internal static readonly AsyncLocal<IFederatedReloginHandler?> TestReloginHandlerOverride = new();

    /// <summary>
    /// Test-only override of the sink that persists a rotated refresh token. When set, it
    /// replaces <see cref="ProfileRefreshTokenSink"/> in the federated provider — used to
    /// exercise the fail-soft path without making the real config file unwritable.
    /// Scoped via <see cref="System.Threading.AsyncLocal{T}"/> to a single test's async context.
    /// </summary>
    internal static readonly AsyncLocal<IRefreshTokenSink?> TestRefreshTokenSinkOverride = new();

    /// <summary>
    /// Builds a <see cref="TrackerContext"/> by loading the config, resolving the effective profile,
    /// instantiating the right <see cref="IAuthProvider"/> and composing the HTTP client pipeline.
    /// </summary>
    /// <param name="profileName">Explicit profile name (from <c>--profile</c>); <c>null</c> falls back to env/config.</param>
    /// <param name="cliReadOnly">Whether the CLI <c>--read-only</c> flag is set.</param>
    /// <param name="timeoutSeconds">Explicit HTTP timeout in seconds (from <c>--timeout</c>); when <c>null</c> uses <c>YT_TIMEOUT</c> or the default.</param>
    /// <param name="wireLogPath">Optional explicit wire-log file path (from <c>--log-file</c>); when <c>null</c> falls back to <c>YT_LOG_FILE</c>.</param>
    /// <param name="wireLogMask">
    /// Whether the wire-log handler should mask sensitive headers and body fields.
    /// Defaults to <c>true</c>. When <c>false</c>, the wire-log captures every value verbatim
    /// (live tokens, OAuth codes, DPoP proofs); intended ONLY for debugging. The CLI also
    /// honours env <c>YT_LOG_RAW</c>, parsed strictly (<c>1</c>/<c>true</c>/<c>yes</c>/<c>on</c>
    /// unmask; <c>0</c>/<c>false</c>/<c>no</c>/<c>off</c> and unset keep masking; anything else
    /// is a <see cref="ErrorCode.ConfigError"/>). The effective setting is
    /// <c>mask = !(cli flag OR env)</c>.
    /// </param>
    /// <param name="innerHandler">Optional HTTP inner handler — for unit tests.</param>
    /// <param name="iamExchangeOverride">Optional IAM exchange client — for unit tests of the service-account flow.</param>
    /// <param name="cliFormat">Значение CLI-флага <c>--format</c>. По умолчанию <see cref="OutputFormat.Auto"/>.
    /// Резолвится через <see cref="FormatResolver"/> с учётом env <c>YT_FORMAT</c>, <c>profile.default_format</c>
    /// и TTY-detection (<see cref="Console.IsOutputRedirected"/>). Доступен через <c>ctx.EffectiveOutputFormat</c>.</param>
    /// <param name="cliNoColor">Whether the CLI <c>--no-color</c> flag is set; влияет на резолв
    /// <see cref="TerminalCapabilities.UseColor"/>.</param>
    /// <param name="cliNoPager">Whether the CLI <c>--no-pager</c> flag is set; отключает pager
    /// в detail/comment view (<see cref="TerminalCapabilities.UsePager"/>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new <see cref="TrackerContext"/> owned by the caller.</returns>
    /// <exception cref="TrackerException">Thrown when the configuration is incomplete or malformed.</exception>
    public static async Task<TrackerContext> CreateAsync(
        string? profileName,
        bool cliReadOnly,
        int? timeoutSeconds,
        string? wireLogPath = null,
        bool wireLogMask = true,
        HttpMessageHandler? innerHandler = null,
        IIamExchangeClient? iamExchangeOverride = null,
        OutputFormat cliFormat = OutputFormat.Auto,
        bool cliNoColor = false,
        bool cliNoPager = false,
        CancellationToken ct = default)
    {
        // Fall back to test-only AsyncLocal overrides when the caller didn't pass explicit instances.
        // This lets end-to-end tests substitute the handler/IAM client without requiring command code
        // to thread those through SetAction.
        innerHandler ??= TestInnerHandlerOverride.Value;
        iamExchangeOverride ??= TestIamExchangeOverride.Value;

        var cfg = await new ConfigStore(ConfigStore.DefaultPath).LoadAsync(ct);
        var env = EnvReader.Snapshot();
        var eff = EnvOverrides.Resolve(cfg, profileName, env, cliReadOnly);

        // Wire-log resolution: explicit --log-file beats env YT_LOG_FILE.
        var resolvedWireLogPath = !string.IsNullOrWhiteSpace(wireLogPath)
            ? wireLogPath
            : env.TryGetValue("YT_LOG_FILE", out var envLog) && !string.IsNullOrWhiteSpace(envLog)
                ? envLog
                : null;

        // Mask resolution: --log-raw (flag, true = unmask) OR YT_LOG_RAW=1 disables masking.
        // Env читается на том же уровне, что и wireLogPath, чтобы скрипту не приходилось
        // переплетать raw-режим через каждую команду. Разбор строгий: снятие маскирования —
        // отказ защиты, поэтому мусор в переменной валит команду, а не тихо льёт токены в файл.
        var rawByEnv = EnvReader.ResolveLogRaw(env);
        var effectiveMask = wireLogMask && !rawByEnv;

        IWireLogSink? wireLogSink = null;
        IAuthProvider auth;
        HttpClient? iamHttp = null;
        RSA? rsa = null;
        ECDsa? ecdsa = null;

        try
        {
            if (resolvedWireLogPath is not null)
            {
                wireLogSink = FileWireLogSink.Create(resolvedWireLogPath);
            }

            switch (eff.Auth.Type)
            {
                case AuthType.OAuth:
                    if (string.IsNullOrWhiteSpace(eff.Auth.Token))
                    {
                        throw new TrackerException(ErrorCode.ConfigError,
                            "OAuth token is not set for the active profile.");
                    }

                    auth = new OAuthProvider(eff.Auth.Token);
                    break;

                case AuthType.IamStatic:
                    if (string.IsNullOrWhiteSpace(eff.Auth.Token))
                    {
                        throw new TrackerException(ErrorCode.ConfigError,
                            "IAM token is not set for the active profile.");
                    }

                    auth = new IamStaticProvider(eff.Auth.Token);
                    break;

                case AuthType.ServiceAccount:
                    if (string.IsNullOrWhiteSpace(eff.Auth.ServiceAccountId)
                        || string.IsNullOrWhiteSpace(eff.Auth.KeyId))
                    {
                        throw new TrackerException(ErrorCode.ConfigError,
                            "Service account id or key id is not set for the active profile.");
                    }

                    rsa = LoadRsa(eff.Auth);
                    iamHttp = BuildAuxHttp(wireLogSink, effectiveMask);
                    var exchange = iamExchangeOverride ?? new IamExchangeClient(iamHttp);
                    var cache = new TokenCache(TokenCache.DefaultPath);
                    var cacheKey = $"{eff.Name}:{eff.Auth.ServiceAccountId}:{eff.Auth.KeyId}";
                    auth = new ServiceAccountProvider(
                        eff.Auth.ServiceAccountId!,
                        eff.Auth.KeyId!,
                        rsa,
                        cache,
                        exchange,
                        cacheKey);
                    break;

                case AuthType.Federated:
                    // Federated profiles arrive in four shapes:
                    //   (A) refresh_token + dpop_key_path  → full DPoP-bound flow w/ auto-refresh
                    //   (B) access token + valid expires_at + no refresh → IamStaticProvider
                    //   (C) access token + no expires_at (legacy)        → IamStaticProvider, trust the token
                    //   (D) anything else (no refresh + no/expired access) → AuthFailed (exit 4)
                    //
                    // We do NOT auto-launch the browser on expiry. The CLI is meant to be
                    // scriptable for AI agents and CI: when this branch decides "expired",
                    // it surfaces an actionable error containing the exact re-login command.
                    if (!string.IsNullOrWhiteSpace(eff.Auth.RefreshToken))
                    {
                        if (string.IsNullOrWhiteSpace(eff.Auth.DpopKeyPath))
                        {
                            throw new TrackerException(
                                ErrorCode.ConfigError,
                                "Federated profile has refresh_token but missing dpop_key_path.");
                        }

                        ecdsa = new DPoPKeyStore(eff.Auth.DpopKeyPath!).LoadOrCreate();
                        iamHttp = BuildAuxHttp(wireLogSink, effectiveMask);
                        var refreshClient = TestFederatedRefreshOverride.Value
                            ?? new FederatedRefreshClient(iamHttp);
                        var fedCache = new TokenCache(TokenCache.DefaultPath);
                        var fedCacheKey = $"{eff.Name}:federated:{eff.Auth.FederationId}";

                        // Interactive TTY → auto-recover from a failed refresh by re-running the
                        // browser flow inline. Non-interactive (agents/CI) → surface an actionable
                        // error naming the exact recovery command. The relogin hint is always set so
                        // the failure message names the command regardless of interactivity.
                        var reloginHint = $"yt auth relogin --profile {eff.Name}";
                        var interactive = TestInteractiveOverride.Value
                            ?? !new ConsoleTokenReader().IsInputRedirected;

                        IFederatedReloginHandler? reloginHandler = null;
                        if (interactive)
                        {
                            reloginHandler = TestReloginHandlerOverride.Value
                                ?? new InlineFederatedReloginHandler(
                                    eff.Name,
                                    cliFormat,
                                    iamHttp,
                                    wireLogSink);
                        }

                        auth = new FederatedTokenProvider(
                            fedCacheKey,
                            ecdsa,
                            fedCache,
                            refreshClient,
                            eff.Auth.RefreshToken!,
                            "yc.oauth.public-sdk",
                            relogin: reloginHandler,
                            reloginCommandHint: reloginHint,
                            refreshTokenSink: TestRefreshTokenSinkOverride.Value
                                ?? new ProfileRefreshTokenSink(eff.Name, eff.Auth.FederationId));
                        break;
                    }

                    // No refresh_token → static-access fallback (or hard fail).
                    if (string.IsNullOrWhiteSpace(eff.Auth.Token))
                    {
                        throw new TrackerException(
                            ErrorCode.AuthFailed,
                            $"Federated profile has no refresh_token and no access token. "
                            + $"Re-login: yt auth login --type federated --profile {eff.Name}");
                    }

                    // expires_at present → enforce a 30-second skew leeway against now().
                    if (!string.IsNullOrWhiteSpace(eff.Auth.AccessTokenExpiresAt))
                    {
                        if (!DateTimeOffset.TryParse(
                                eff.Auth.AccessTokenExpiresAt,
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                                out var expiresAt))
                        {
                            throw new TrackerException(
                                ErrorCode.ConfigError,
                                $"Federated profile has invalid access_token_expires_at: '{eff.Auth.AccessTokenExpiresAt}'.");
                        }

                        var leeway = TimeSpan.FromSeconds(30);
                        if (DateTimeOffset.UtcNow >= expiresAt - leeway)
                        {
                            throw new TrackerException(
                                ErrorCode.AuthFailed,
                                $"Federated access token expired. "
                                + $"Re-login: yt auth login --type federated --profile {eff.Name}");
                        }
                    }
                    // else: legacy profile without expires_at — trust the token until the API
                    //       hands back a 401 (which will be surfaced as AuthFailed by the API layer).

                    auth = new IamStaticProvider(eff.Auth.Token!);
                    break;

                default:
                    throw new TrackerException(ErrorCode.ConfigError,
                        $"Unsupported auth type: {eff.Auth.Type}.");
            }

            Uri? baseUrl = null;
            if (env.TryGetValue("YT_API_BASE_URL", out var b) && !string.IsNullOrWhiteSpace(b))
            {
                baseUrl = new Uri(b);
            }

            // Каскад --timeout → YT_TIMEOUT → дефолт живёт в TimeoutResolver: то же значение
            // называет пользователю сообщение об отмене, и разойтись они не могут.
            var timeout = TimeSpan.FromSeconds(TimeoutResolver.Resolve(timeoutSeconds, env));

            var http = TrackerHttpClientFactory.Create(
                eff,
                auth,
                innerHandler,
                baseUrl: baseUrl,
                timeout: timeout,
                wireLogSink: wireLogSink,
                wireLogMask: effectiveMask);

            var client = new TrackerClient(http);

            var effectiveFormat = FormatResolver.Resolve(
                cliFormat,
                env,
                profileDefaultFormat: eff.DefaultFormat,
                isOutputRedirected: Console.IsOutputRedirected);

            var caps = TerminalCapabilities.Detect(
                env,
                noColorFlag: cliNoColor,
                noPagerFlag: cliNoPager,
                isOutputRedirected: () => Console.IsOutputRedirected,
                consoleWidth: () =>
                {
                    try
                    {
                        var w = Console.WindowWidth;
                        return w > 0 ? w : (int?)null;
                    }
                    catch
                    {
                        return null;
                    }
                });

            return new TrackerContext(
                client, eff, http, iamHttp, rsa, ecdsa, wireLogSink,
                effectiveFormat,
                terminalCapabilities: caps);
        }
        catch
        {
            // Ownership of disposables hasn't transferred to the context yet — release them.
            iamHttp?.Dispose();
            rsa?.Dispose();
            ecdsa?.Dispose();
            if (wireLogSink is not null)
            {
                wireLogSink.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            throw;
        }
    }

    /// <summary>
    /// Builds an auxiliary <see cref="HttpClient"/> for IAM exchange / federated refresh,
    /// optionally wrapping it with a <see cref="WireLogHandler"/> when wire-log capture is on.
    /// </summary>
    /// <param name="sink">Active wire-log sink, or <c>null</c> if logging is disabled.</param>
    /// <param name="maskSensitive">Whether the wire-log handler should mask secrets.</param>
    private static HttpClient BuildAuxHttp(IWireLogSink? sink, bool maskSensitive)
    {
        if (sink is null)
        {
            return new HttpClient();
        }

        var wire = new WireLogHandler(sink, maskSensitive: maskSensitive) { InnerHandler = new SocketsHttpHandler() };
        return new HttpClient(wire, disposeHandler: true);
    }

    private static RSA LoadRsa(AuthConfig a)
    {
        string pem;
        if (!string.IsNullOrWhiteSpace(a.PrivateKeyPem))
        {
            pem = a.PrivateKeyPem!;
        }
        else if (!string.IsNullOrWhiteSpace(a.PrivateKeyPath))
        {
            pem = File.ReadAllText(a.PrivateKeyPath!);
        }
        else
        {
            throw new TrackerException(ErrorCode.ConfigError, "Service account key not set.");
        }

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }
}

/// <summary>
/// Production <see cref="IRefreshTokenSink"/>: writes a rotated refresh token back into the
/// active profile, leaving every other field — other profiles, the default-profile selection,
/// the profile's policy and org settings, its access token and DPoP key path — untouched.
/// </summary>
/// <remarks>
/// The write goes through <see cref="ConfigStore.ModifyAsync(Func{ConfigFile, ConfigFile}, TimeSpan?, CancellationToken)"/>,
/// which re-reads the file under the cross-process lock, so a parallel <c>yt</c> editing an
/// unrelated part of the config does not lose its update. The lock cannot prevent two processes
/// from refreshing the same profile at once — see the remarks on the caller in
/// <c>FederatedTokenProvider</c> — so the write is additionally a compare-and-swap on the
/// refresh token itself: if the profile no longer holds the token this exchange was started
/// from, someone else has moved it on and this (older) token must not land on top.
/// </remarks>
internal sealed class ProfileRefreshTokenSink : IRefreshTokenSink
{
    private readonly string _profileName;
    private readonly string? _federationId;

    /// <summary>
    /// Initializes a new <see cref="ProfileRefreshTokenSink"/>.
    /// </summary>
    /// <param name="profileName">Profile whose refresh token is being rotated.</param>
    /// <param name="federationId">
    /// Federation id the in-memory token belongs to; re-checked against the fresh snapshot so
    /// that a profile recreated meanwhile (different federation, or a switch to another auth
    /// type) is not overwritten with a token that does not belong to it.
    /// </param>
    public ProfileRefreshTokenSink(string profileName, string? federationId)
    {
        _profileName = profileName;
        _federationId = federationId;
    }

    /// <inheritdoc />
    public async Task SaveRefreshToken(string expectedCurrentToken, string refreshToken, CancellationToken ct)
    {
        var store = new ConfigStore(ConfigStore.DefaultPath);

        await store.ModifyAsync(
            fresh =>
            {
                if (!fresh.Profiles.TryGetValue(_profileName, out var current))
                {
                    throw new TrackerException(
                        ErrorCode.ConfigError,
                        $"Profile '{_profileName}' is no longer present in the configuration.");
                }

                if (current.Auth.Type != AuthType.Federated
                    || !string.Equals(current.Auth.FederationId, _federationId, StringComparison.Ordinal))
                {
                    throw new TrackerException(
                        ErrorCode.ConfigError,
                        $"Profile '{_profileName}' changed while the token was being refreshed "
                        + $"(expected federated/'{_federationId}', found {current.Auth.Type}/'{current.Auth.FederationId}').");
                }

                if (string.Equals(current.Auth.RefreshToken, refreshToken, StringComparison.Ordinal))
                {
                    // Already exactly this token on disk: nothing to write. Returning the
                    // snapshot unchanged tells ModifyAsync to skip the rewrite entirely.
                    return fresh;
                }

                if (!string.Equals(current.Auth.RefreshToken, expectedCurrentToken, StringComparison.Ordinal))
                {
                    // Compare-and-swap failed: between the exchange and this write another run
                    // (a parallel refresh, or `yt auth relogin` in a second terminal) replaced
                    // the profile's credentials. Its refresh token is newer than the one in
                    // hand, and it came with a matching access token and expiry. Writing here
                    // would bury a live token under a superseded one and leave the profile
                    // half from one session, half from another — silently, since the write
                    // itself would succeed. Refusing costs at most one re-login, and says so.
                    throw new TrackerException(
                        ErrorCode.ConfigError,
                        $"Profile '{_profileName}' was re-authenticated by another run while this "
                        + "token was being refreshed; its refresh token is newer and was kept.");
                }

                var profiles = new Dictionary<string, Core.Config.Profile>(fresh.Profiles)
                {
                    [_profileName] = current with { Auth = current.Auth with { RefreshToken = refreshToken } },
                };
                return new ConfigFile(fresh.DefaultProfile, profiles);
            },
            // Between the ten-second default and the minute used after an interactive login:
            // the credential at stake is just as irreplaceable as there, but the wait lands in
            // the middle of an ordinary command. See ConfigStore.RotatedTokenLockTimeout.
            ConfigStore.RotatedTokenLockTimeout,
            ct);
    }
}

/// <summary>
/// Production <see cref="IFederatedReloginHandler"/> used by <see cref="TrackerContextFactory"/>
/// in interactive (TTY) mode: on a failed refresh it opens the system browser, re-runs the
/// federated PKCE+DPoP flow for the active profile, persists the new tokens, and returns them.
/// </summary>
internal sealed class InlineFederatedReloginHandler : IFederatedReloginHandler
{
    private readonly string _profileName;
    private readonly OutputFormat _cliFormat;
    private readonly HttpClient _http;
    private readonly IWireLogSink? _wireLogSink;

    /// <summary>
    /// Initializes a new <see cref="InlineFederatedReloginHandler"/>.
    /// </summary>
    /// <param name="profileName">Active profile name to re-login.</param>
    /// <param name="cliFormat">CLI <c>--format</c> value used to resolve the interactive UI.</param>
    /// <param name="http">Shared auxiliary <see cref="HttpClient"/> (reused for the token exchange).</param>
    /// <param name="wireLogSink">Optional wire-log sink to capture the authorize URL.</param>
    public InlineFederatedReloginHandler(
        string profileName,
        OutputFormat cliFormat,
        HttpClient http,
        IWireLogSink? wireLogSink)
    {
        _profileName = profileName;
        _cliFormat = cliFormat;
        _http = http;
        _wireLogSink = wireLogSink;
    }

    /// <inheritdoc />
    public Task<FederatedTokenResult> ReloginAsync(CancellationToken ct)
    {
        var env = EnvReader.Snapshot();
        var format = FormatResolver.Resolve(
            _cliFormat,
            env,
            profileDefaultFormat: null,
            isOutputRedirected: Console.IsOutputRedirected);
        var ui = InteractiveUIResolver.Resolve(format);
        IBrowserLauncher launcher = FederatedReloginService.TestBrowserLauncher.Value
            ?? new SystemBrowserLauncher();

        return FederatedReloginService.ReloginAsync(
            _profileName,
            launcher,
            ui,
            _http,
            _wireLogSink,
            TimeSpan.FromSeconds(120),
            ct);
    }
}
