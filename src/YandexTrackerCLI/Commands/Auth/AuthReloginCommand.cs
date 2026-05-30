namespace YandexTrackerCLI.Commands.Auth;

using System.CommandLine;
using System.Net.Http;
using Core.Api.Errors;
using YandexTrackerCLI.Auth.Federated;
using Interactive;
using Core.Http;
using Output;

/// <summary>
/// Команда <c>yt auth relogin</c>: повторный браузерный вход для существующего
/// federated-профиля. Переиспользует сохранённые <c>federation_id</c> и DPoP-ключ
/// профиля — не нужно повторно указывать <c>--federation-id</c>/<c>--org-*</c>.
/// </summary>
/// <remarks>
/// В отличие от <c>auth login --type federated</c>, эта команда не читает stdin и
/// поэтому НЕ требует TTY: она лишь открывает браузер и ждёт OAuth callback, что
/// одинаково работает в интерактивном и non-TTY окружении.
/// </remarks>
public static class AuthReloginCommand
{
    /// <summary>
    /// Строит subcommand <c>relogin</c> для <c>yt auth</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var timeoutAuthOption = new Option<int>("--timeout-auth")
        {
            Description = "Таймаут ожидания callback браузера, сек (default 120).",
            DefaultValueFactory = _ => 120,
        };

        var cmd = new Command(
            "relogin",
            "Повторный браузерный вход для federated-профиля (переиспользует federation_id и DPoP-ключ).");
        cmd.Options.Add(timeoutAuthOption);

        cmd.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var profileName = parseResult.GetValue(RootCommandBuilder.ProfileOption) ?? "default";
                var timeoutAuthSec = parseResult.GetValue(timeoutAuthOption);

                var effectiveFormat = CommandFormatHelper.ResolveForCommand(parseResult);
                var ui = InteractiveUIResolver.Resolve(effectiveFormat);

                var wireLogPath = parseResult.GetValue(RootCommandBuilder.LogFileOption)
                    ?? Environment.GetEnvironmentVariable("YT_LOG_FILE");
                var wireLogMask = !parseResult.GetValue(RootCommandBuilder.LogRawOption)
                    && !IsTruthyEnv("YT_LOG_RAW");

                var launcher = FederatedReloginService.TestBrowserLauncher.Value
                    ?? AuthLoginCommand.TestBrowserLauncher.Value
                    ?? new SystemBrowserLauncher();

                var ownsHttp = FederatedReloginService.TestFederatedHttpClient.Value is null
                    && AuthLoginCommand.TestFederatedHttpClient.Value is null;
                IWireLogSink? wireSink = null;
                HttpClient http;
                if (FederatedReloginService.TestFederatedHttpClient.Value is { } svcHttp)
                {
                    http = svcHttp;
                }
                else if (AuthLoginCommand.TestFederatedHttpClient.Value is { } loginHttp)
                {
                    http = loginHttp;
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

                try
                {
                    var result = await FederatedReloginService.ReloginAsync(
                        profileName,
                        launcher,
                        ui,
                        http,
                        wireSink,
                        TimeSpan.FromSeconds(timeoutAuthSec),
                        ct);

                    // ReloginAsync already persisted the authoritative profile (federation_id,
                    // DPoP key, refreshed tokens). Here we only emit the success-marker, which
                    // reflects refresh capability + access-token expiry.
                    var hasRefreshToken = !string.IsNullOrEmpty(result.RefreshToken);
                    var expiresAtIso = result.ExpiresAt.ToUniversalTime().ToString("O");

                    if (!hasRefreshToken)
                    {
                        AuthLoginCommand.WriteNoRefreshTokenWarning(expiresAtIso);
                    }

                    AuthLoginCommand.WriteFederatedSavedMarker(profileName, hasRefreshToken, expiresAtIso);
                    return 0;
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
            catch (TrackerException ex)
            {
                ErrorWriter.Write(Console.Error, ex);
                return ex.Code.ToExitCode();
            }
        });

        return cmd;
    }

    /// <summary>
    /// Reads the named environment variable and returns <c>true</c> when its value is a
    /// "truthy" boolean string (anything other than <c>null</c>, empty, <c>0</c>, <c>false</c>,
    /// <c>no</c>, <c>off</c>; case-insensitive).
    /// </summary>
    /// <param name="name">Environment variable name.</param>
    /// <returns><c>true</c> when the value is truthy.</returns>
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
