namespace YandexTrackerCLI.Core.Http;

using System.Runtime.InteropServices;
using Auth;
using Config;

/// <summary>
/// Builds a pre-configured <see cref="HttpClient"/> for the Yandex Tracker API by composing
/// the delegating handler chain:
/// Retry -&gt; ReadOnlyGuard -&gt; OrgHeader -&gt; DPoP -&gt; Auth -&gt; inner transport.
/// </summary>
public static class TrackerHttpClientFactory
{
    /// <summary>
    /// Default base address of the Yandex Tracker API (v3).
    /// </summary>
    public static readonly Uri DefaultBaseUrl = new("https://api.tracker.yandex.net/v3/");

    /// <summary>
    /// Default request timeout applied when no explicit value is provided.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates an <see cref="HttpClient"/> wired with the Tracker handler chain, base address,
    /// timeout and a <c>User-Agent</c> identifying the CLI.
    /// </summary>
    /// <param name="profile">The effective profile carrying org type, org id and read-only flag.</param>
    /// <param name="authProvider">Provider that supplies the <c>Authorization</c> header per request.</param>
    /// <param name="innerHandler">
    /// Optional inner transport handler (used in tests). Defaults to a fresh <see cref="SocketsHttpHandler"/>.
    /// </param>
    /// <param name="baseUrl">Optional base address override; defaults to <see cref="DefaultBaseUrl"/>.</param>
    /// <param name="timeout">Optional request timeout override; defaults to <see cref="DefaultTimeout"/>.</param>
    /// <param name="wireLogSink">
    /// Optional sink that, when supplied, captures every HTTP request and response between
    /// the auth/header handlers and the transport. Installed as the innermost
    /// <see cref="DelegatingHandler"/> so the recorded headers and body match the wire.
    /// </param>
    /// <param name="wireLogMask">
    /// When <c>true</c> (default), <see cref="WireLogHandler"/> masks sensitive headers and
    /// body fields. When <c>false</c>, every value is logged verbatim — used only for
    /// debugging protocol issues such as DPoP thumbprint mismatches.
    /// </param>
    /// <returns>A configured <see cref="HttpClient"/> that owns and disposes the handler chain.</returns>
    public static HttpClient Create(
        EffectiveProfile profile,
        IAuthProvider authProvider,
        HttpMessageHandler? innerHandler = null,
        Uri? baseUrl = null,
        TimeSpan? timeout = null,
        IWireLogSink? wireLogSink = null,
        bool wireLogMask = true)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(authProvider);

        HttpMessageHandler chain = innerHandler ?? new SocketsHttpHandler();

        if (wireLogSink is not null)
        {
            chain = new WireLogHandler(wireLogSink, maskSensitive: wireLogMask) { InnerHandler = chain };
        }

        chain = new AuthHandler(authProvider) { InnerHandler = chain };
        chain = new DPoPHandler() { InnerHandler = chain };
        chain = new OrgHeaderHandler(profile.OrgType, profile.OrgId) { InnerHandler = chain };
        chain = new ReadOnlyGuardHandler(profile.ReadOnly) { InnerHandler = chain };
        chain = new RetryHandler() { InnerHandler = chain };

        var http = new HttpClient(chain, disposeHandler: true)
        {
            BaseAddress = baseUrl ?? DefaultBaseUrl,
            Timeout = timeout ?? DefaultTimeout,
        };

        http.DefaultRequestHeaders.UserAgent.ParseAdd(BuildUserAgent());
        return http;
    }

    private static string BuildUserAgent()
    {
        var version = typeof(TrackerHttpClientFactory).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var os = SanitizeToken(RuntimeInformation.OSDescription);
        var arch = RuntimeInformation.OSArchitecture;
        return $"yandex-tracker-cli/{version} ({os}; {arch})";
    }

    private static string SanitizeToken(string value)
    {
        // User-Agent product comment must be a valid token/comment; replace whitespace and
        // any control characters to keep ParseAdd happy across platforms (Darwin, Linux, Windows).
        Span<char> buf = stackalloc char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            buf[i] = char.IsWhiteSpace(c) || char.IsControl(c) ? '-' : c;
        }

        return new string(buf);
    }
}
