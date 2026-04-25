namespace YandexTrackerCLI.Commands.Auth;

using System.CommandLine;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Core.Api.Errors;
using YandexTrackerCLI.Auth.Federated;
using Interactive;
using YandexTrackerCLI.Core.Config;
using Core.Http;
using Output;

/// <summary>
/// Команда <c>yt auth login</c>: сохраняет креды для выбранного типа аутентификации
/// в профиль конфигурационного файла.
/// </summary>
/// <remarks>
/// Поддерживает четыре типа аутентификации через <c>--type</c>:
/// <list type="bullet">
///   <item><description><c>oauth</c> — если указан <c>--token</c>, сохраняется напрямую;
///   иначе в TTY запускается интерактивный flow через браузер +
///   чтение токена со stdin; в non-TTY возвращает <c>invalid_args</c>.</description></item>
///   <item><description><c>iam-static</c> — требует <c>--token</c>.</description></item>
///   <item><description><c>service-account</c> — требует <c>--sa-id</c>, <c>--key-id</c> и либо <c>--key-file</c>, либо <c>--key-pem</c>.</description></item>
///   <item><description><c>federated</c> — browser PKCE flow с <c>--federation-id</c>
///   (по мотивам <c>yc init --federation-id</c>); требует TTY.</description></item>
/// </list>
/// </remarks>
public static class AuthLoginCommand
{
    /// <summary>
    /// Плейсхолдер client_id по умолчанию. Реальное значение задаётся через
    /// <c>--client-id</c> или переменную окружения <c>YT_OAUTH_CLIENT_ID</c>
    /// (client_id зарегистрированного приложения в Яндекс OAuth).
    /// </summary>
    internal const string DefaultClientId = "REPLACE_WITH_REGISTERED_CLIENT_ID";

    /// <summary>
    /// Default public client id для federated flow (по аналогии с <c>yc</c>).
    /// </summary>
    internal const string FederatedDefaultClientId = "yc.oauth.public-sdk";

    /// <summary>
    /// Test-override для <see cref="IBrowserLauncher"/>: в тестах можно
    /// подставить фейк, чтобы не запускать реальный браузер.
    /// </summary>
    internal static readonly AsyncLocal<IBrowserLauncher?> TestBrowserLauncher = new();

    /// <summary>
    /// Test-override для <see cref="ITokenReader"/>: в тестах подменяется
    /// фейковой реализацией с пред-заданной очередью строк.
    /// </summary>
    internal static readonly AsyncLocal<ITokenReader?> TestTokenReader = new();

    /// <summary>
    /// Test-override для <see cref="HttpClient"/>, используемого при
    /// обмене <c>code → access_token</c> в federated flow.
    /// </summary>
    internal static readonly AsyncLocal<HttpClient?> TestFederatedHttpClient = new();

    /// <summary>
    /// Строит subcommand <c>login</c> для <c>yt auth</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var typeOption = new Option<string>("--type")
        {
            Description = "oauth | iam-static | service-account | federated",
            Required = true,
        };
        typeOption.AcceptOnlyFromAmong("oauth", "iam-static", "service-account", "federated");

        var tokenOption = new Option<string?>("--token")
        {
            Description = "OAuth или IAM-токен.",
        };
        var saIdOption = new Option<string?>("--sa-id")
        {
            Description = "Service account ID.",
        };
        var keyIdOption = new Option<string?>("--key-id")
        {
            Description = "Authorized key ID.",
        };
        var keyFileOption = new Option<string?>("--key-file")
        {
            Description = "Путь к PEM приватного ключа.",
        };
        var keyPemOption = new Option<string?>("--key-pem")
        {
            Description = "Inline PEM (предпочтительнее --key-file).",
        };
        var clientIdOption = new Option<string?>("--client-id")
        {
            Description = "OAuth client_id (override для интерактивного flow).",
        };
        var federationIdOption = new Option<string?>("--federation-id")
        {
            Description = "ID федерации в Yandex Cloud (обязателен для --type federated).",
        };
        var timeoutAuthOption = new Option<int>("--timeout-auth")
        {
            Description = "Таймаут ожидания callback браузера, сек (default 120).",
            DefaultValueFactory = _ => 120,
        };

        var orgTypeOption = new Option<string>("--org-type")
        {
            Description = "yandex360 | cloud",
            Required = true,
        };
        orgTypeOption.AcceptOnlyFromAmong("yandex360", "cloud");

        var orgIdOption = new Option<string>("--org-id")
        {
            Description = "Идентификатор организации.",
            Required = true,
        };

        var cmd = new Command("login", "Сохранить креды в профиль (non-interactive).");
        cmd.Options.Add(typeOption);
        cmd.Options.Add(tokenOption);
        cmd.Options.Add(saIdOption);
        cmd.Options.Add(keyIdOption);
        cmd.Options.Add(keyFileOption);
        cmd.Options.Add(keyPemOption);
        cmd.Options.Add(clientIdOption);
        cmd.Options.Add(federationIdOption);
        cmd.Options.Add(timeoutAuthOption);
        cmd.Options.Add(orgTypeOption);
        cmd.Options.Add(orgIdOption);

        cmd.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var type = parseResult.GetValue(typeOption)!;
                var token = parseResult.GetValue(tokenOption);
                var saId = parseResult.GetValue(saIdOption);
                var keyId = parseResult.GetValue(keyIdOption);
                var keyFile = parseResult.GetValue(keyFileOption);
                var keyPem = parseResult.GetValue(keyPemOption);
                var clientId = parseResult.GetValue(clientIdOption);
                var federationId = parseResult.GetValue(federationIdOption);
                var timeoutAuthSec = parseResult.GetValue(timeoutAuthOption);
                var orgType = parseResult.GetValue(orgTypeOption)! == "cloud"
                    ? OrgType.Cloud
                    : OrgType.Yandex360;
                var orgId = parseResult.GetValue(orgIdOption)!;
                var profileName = parseResult.GetValue(RootCommandBuilder.ProfileOption) ?? "default";

                if (type == "oauth" && string.IsNullOrWhiteSpace(token))
                {
                    token = await ResolveOAuthTokenInteractive(clientId, ct);
                }

                var wireLogPath = parseResult.GetValue(RootCommandBuilder.LogFileOption)
                    ?? Environment.GetEnvironmentVariable("YT_LOG_FILE");
                var wireLogMask = !parseResult.GetValue(RootCommandBuilder.LogRawOption)
                    && !IsTruthyEnv("YT_LOG_RAW");

                AuthConfig auth = type switch
                {
                    "oauth"           => BuildOAuth(token),
                    "iam-static"      => BuildIamStatic(token),
                    "service-account" => BuildSa(saId, keyId, keyFile, keyPem),
                    "federated"       => await BuildFederated(federationId, clientId, timeoutAuthSec, profileName, wireLogPath, wireLogMask, ct),
                    _ => throw new TrackerException(ErrorCode.InvalidArgs, $"Unknown --type {type}"),
                };

                var store = new ConfigStore(ConfigStore.DefaultPath);
                var cfg = await store.LoadAsync(ct);
                // Preserve default_format if the profile already exists — re-login should not
                // clobber user output preferences.
                cfg.Profiles.TryGetValue(profileName, out var prevProfile);
                var profiles = new Dictionary<string, Profile>(cfg.Profiles)
                {
                    [profileName] = new Profile(
                        orgType,
                        orgId,
                        ReadOnly: false,
                        auth,
                        DefaultFormat: prevProfile?.DefaultFormat),
                };
                var defaultName = string.IsNullOrWhiteSpace(cfg.DefaultProfile) ? profileName : cfg.DefaultProfile;
                await store.SaveAsync(new ConfigFile(defaultName, profiles), ct);

                if (auth.Type == AuthType.Federated)
                {
                    // Federated success-marker carries `mode` so callers can distinguish
                    // full DPoP-bound (with auto-refresh) from access-only fallback.
                    WriteFederatedSavedMarker(profileName, auth);
                }
                else
                {
                    CommandOutput.WriteSingleField("saved", profileName);
                }
                return 0;
            }
            catch (TrackerException ex)
            {
                ErrorWriter.Write(Console.Error, ex);
                return ex.Code.ToExitCode();
            }
        });

        return cmd;
    }

    private static AuthConfig BuildOAuth(string? token) =>
        string.IsNullOrWhiteSpace(token)
            ? throw new TrackerException(
                ErrorCode.InvalidArgs,
                "--token is required for --type oauth.")
            : new AuthConfig(AuthType.OAuth, Token: token);

    /// <summary>
    /// Интерактивный OAuth-flow: открывает <c>oauth.yandex.ru/authorize</c> в
    /// браузере и читает вставленный пользователем токен со stdin.
    /// </summary>
    /// <param name="clientId">CLI-override для client_id; <c>null</c> → env var → default.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Полученный OAuth-токен (trimmed, непустой).</returns>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.InvalidArgs"/> — stdin перенаправлен (не TTY);
    /// <see cref="ErrorCode.AuthFailed"/> — пустой ввод после 3 попыток.
    /// </exception>
    private static async Task<string> ResolveOAuthTokenInteractive(string? clientId, CancellationToken ct)
    {
        var reader = TestTokenReader.Value ?? new ConsoleTokenReader();
        var launcher = TestBrowserLauncher.Value ?? new SystemBrowserLauncher();

        if (reader.IsInputRedirected)
        {
            throw new TrackerException(
                ErrorCode.InvalidArgs,
                "--token is required in non-interactive mode. Use --token <...> or run in a TTY.");
        }

        var effectiveClientId = !string.IsNullOrWhiteSpace(clientId)
            ? clientId
            : Environment.GetEnvironmentVariable("YT_OAUTH_CLIENT_ID") ?? DefaultClientId;
        var url = $"https://oauth.yandex.ru/authorize?response_type=token&client_id={Uri.EscapeDataString(effectiveClientId)}";

        await launcher.OpenAsync(url, ct);
        Console.Error.WriteLine($"Browser opened: {url}");
        Console.Error.WriteLine("Paste the OAuth token and press Enter:");

        string? attempt = null;
        for (var i = 0; i < 3; i++)
        {
            attempt = reader.ReadLine()?.Trim();
            if (!string.IsNullOrWhiteSpace(attempt))
            {
                break;
            }
            Console.Error.WriteLine("Empty input. Try again:");
        }

        if (string.IsNullOrWhiteSpace(attempt))
        {
            throw new TrackerException(ErrorCode.AuthFailed, "No OAuth token provided after 3 attempts.");
        }

        return attempt;
    }

    /// <summary>
    /// Ветка <c>--type federated</c>: browser PKCE flow с <c>yc_federation_hint</c>,
    /// с последующей генерацией DPoP key pair (ECDSA P-256) для RFC 9449 refresh flow.
    /// </summary>
    /// <param name="federationId">Обязательный ID федерации.</param>
    /// <param name="clientId">Override OAuth client_id; по умолчанию <see cref="FederatedDefaultClientId"/>.</param>
    /// <param name="timeoutSec">Таймаут ожидания callback, сек.</param>
    /// <param name="profileName">Имя профиля — используется как ключ для DPoP keystore.</param>
    /// <param name="wireLogPath">Optional wire-log file path for капture token-exchange traffic.</param>
    /// <param name="wireLogMask">When <c>true</c> (default), the wire-log handler masks sensitive
    /// headers and body fields. When <c>false</c>, every value is logged verbatim — the same
    /// sink is also handed to <see cref="FederatedOAuthFlow.RunAsync"/> so the authorize URL
    /// (which is opened in the browser, not via our HttpClient) is also captured.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Построенный <see cref="AuthConfig"/> типа <see cref="AuthType.Federated"/>
    /// с access/refresh/federation_id/dpop_key_path.</returns>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.InvalidArgs"/> — отсутствует <c>--federation-id</c> или stdin не TTY;
    /// <see cref="ErrorCode.AuthFailed"/> — ошибки PKCE/обмена code→token.
    /// </exception>
    private static async Task<AuthConfig> BuildFederated(
        string? federationId,
        string? clientId,
        int timeoutSec,
        string profileName,
        string? wireLogPath,
        bool wireLogMask,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(federationId))
        {
            throw new TrackerException(
                ErrorCode.InvalidArgs,
                "--federation-id is required for --type federated.");
        }

        var reader = TestTokenReader.Value ?? new ConsoleTokenReader();
        if (reader.IsInputRedirected)
        {
            throw new TrackerException(
                ErrorCode.InvalidArgs,
                "--type federated requires an interactive TTY.");
        }

        var launcher = TestBrowserLauncher.Value ?? new SystemBrowserLauncher();
        var ownsHttp = TestFederatedHttpClient.Value is null;
        IWireLogSink? wireSink = null;
        HttpClient http;
        if (TestFederatedHttpClient.Value is not null)
        {
            http = TestFederatedHttpClient.Value;
        }
        else if (!string.IsNullOrWhiteSpace(wireLogPath))
        {
            wireSink = FileWireLogSink.Create(wireLogPath);
            var wire = new WireLogHandler(wireSink, maskSensitive: wireLogMask) { InnerHandler = new SocketsHttpHandler() };
            http = new HttpClient(wire, disposeHandler: true);
        }
        else
        {
            http = new HttpClient();
        }

        // Materialize the DPoP key pair BEFORE the OAuth flow: the public-key thumbprint
        // is sent on the authorize request as `dpop_jkt`, which signals Yandex Cloud to
        // bind the issued tokens (and refresh_token) to this key. The same key must be
        // reused for the lifetime of the refresh token.
        var keyPath = DPoPKeyStore.DefaultPathForProfile(profileName);
        var keyStore = new DPoPKeyStore(keyPath);
        using var dpopKey = keyStore.LoadOrCreate();

        try
        {
            var effectiveClientId = !string.IsNullOrWhiteSpace(clientId)
                ? clientId
                : FederatedDefaultClientId;
            var timeout = TimeSpan.FromSeconds(timeoutSec);

            var result = await FederatedOAuthFlow.RunAsync(
                federationId,
                effectiveClientId,
                dpopKey,
                launcher,
                http,
                timeout,
                ct,
                wireLogSink: wireSink);

            var expiresAtIso = result.ExpiresAt.ToUniversalTime().ToString("O");

            // Yandex Cloud may legitimately omit refresh_token (organization policy disabled
            // it, or DPoP-bound flow was unavailable). We persist the access token, mark the
            // expiration so the factory can detect "expired"/"alive" without a network call,
            // and emit a single structured warning to stderr so scripts can react.
            if (string.IsNullOrEmpty(result.RefreshToken))
            {
                WriteNoRefreshTokenWarning(expiresAtIso);
            }

            return new AuthConfig(
                AuthType.Federated,
                Token: result.AccessToken,
                RefreshToken: result.RefreshToken,
                FederationId: federationId,
                DpopKeyPath: keyPath,
                AccessTokenExpiresAt: expiresAtIso);
        }
        finally
        {
            if (ownsHttp)
            {
                http.Dispose();
            }
            if (wireSink is not null)
            {
                await wireSink.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Emits a structured warning JSON line on <see cref="Console.Error"/> describing the
    /// degraded "no refresh_token" mode. Format mirrors the error envelope so existing
    /// JSON parsers (jq, agents) can consume it: a single object with a top-level
    /// <c>warning</c> key and stable fields under it.
    /// </summary>
    private static void WriteNoRefreshTokenWarning(string accessTokenExpiresAtIso)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteStartObject("warning");
            w.WriteString("code", "no_refresh_token");
            w.WriteString(
                "message",
                "Server did not issue a refresh_token. Re-login will be required after access token expires (~12h).");
            w.WriteString("access_token_expires_at", accessTokenExpiresAtIso);
            w.WriteEndObject();
            w.WriteEndObject();
        }

        Console.Error.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
    }

    /// <summary>
    /// Emits the federated-login success-marker on <see cref="Console.Out"/>:
    /// <c>{"saved":"&lt;profile&gt;","mode":"federated|federated_static","access_token_expires_at":"..."}</c>.
    /// </summary>
    /// <param name="profileName">Profile that was just persisted.</param>
    /// <param name="auth">The auth config that was just saved (carries refresh-presence + expiry).</param>
    private static void WriteFederatedSavedMarker(string profileName, AuthConfig auth)
    {
        var mode = string.IsNullOrEmpty(auth.RefreshToken) ? "federated_static" : "federated";

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("saved", profileName);
            w.WriteString("mode", mode);
            if (!string.IsNullOrEmpty(auth.AccessTokenExpiresAt))
            {
                w.WriteString("access_token_expires_at", auth.AccessTokenExpiresAt);
            }
            w.WriteEndObject();
        }

        Console.Out.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
    }

    private static AuthConfig BuildIamStatic(string? token) =>
        string.IsNullOrWhiteSpace(token)
            ? throw new TrackerException(ErrorCode.InvalidArgs, "--token is required for --type iam-static.")
            : new AuthConfig(AuthType.IamStatic, Token: token);

    private static AuthConfig BuildSa(string? saId, string? keyId, string? keyFile, string? keyPem)
    {
        if (string.IsNullOrWhiteSpace(saId) || string.IsNullOrWhiteSpace(keyId)
            || (string.IsNullOrWhiteSpace(keyFile) && string.IsNullOrWhiteSpace(keyPem)))
        {
            throw new TrackerException(
                ErrorCode.InvalidArgs,
                "--sa-id, --key-id and (--key-file or --key-pem) are required for service-account.");
        }

        return new AuthConfig(
            AuthType.ServiceAccount,
            ServiceAccountId: saId,
            KeyId: keyId,
            PrivateKeyPath: keyFile,
            PrivateKeyPem: keyPem);
    }

    /// <summary>
    /// Reads the named environment variable and returns <c>true</c> when its value is a
    /// "truthy" boolean string (anything other than <c>null</c>, empty, <c>0</c>, <c>false</c>,
    /// <c>no</c>, <c>off</c>; case-insensitive).
    /// </summary>
    private static bool IsTruthyEnv(string name)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var v = raw.Trim();
        if (string.Equals(v, "0", StringComparison.Ordinal)
            || string.Equals(v, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
