namespace YandexTrackerCLI.Auth.Federated;

using System.Net.Http;
using System.Threading;
using Core.Api.Errors;
using Interactive;
using YandexTrackerCLI.Core.Config;
using Core.Http;
using Profile = YandexTrackerCLI.Core.Config.Profile;

/// <summary>
/// Re-runs the federated browser PKCE+DPoP login flow for an <b>already-existing</b>
/// profile and persists the freshly issued tokens back into that profile, preserving
/// all other profiles, the default-profile selection, and the profile's non-auth
/// settings (org type/id, read-only, default output format).
/// </summary>
/// <remarks>
/// Используется в двух местах:
/// <list type="bullet">
///   <item><description>командой <c>yt auth relogin</c> — явный повторный вход;</description></item>
///   <item><description>инлайн-обработчиком в <see cref="FederatedTokenProvider"/> — когда в
///   интерактивном TTY refresh-токен протух/отозван, CLI сам перезапускает браузерный flow.</description></item>
/// </list>
/// В отличие от <c>auth login</c>, повторно используется уже сохранённый DPoP-ключ профиля
/// (сервер перевыпускает токены, привязанные к тому же <c>dpop_jkt</c>) и не требуется
/// повторно указывать <c>--federation-id</c>.
/// </remarks>
public static class FederatedReloginService
{
    /// <summary>
    /// Test-override для <see cref="IBrowserLauncher"/>: в тестах подставляется фейк,
    /// чтобы не запускать реальный браузер.
    /// </summary>
    internal static readonly AsyncLocal<IBrowserLauncher?> TestBrowserLauncher = new();

    /// <summary>
    /// Test-override для <see cref="HttpClient"/>, используемого при обмене
    /// <c>code → access_token</c> в federated flow.
    /// </summary>
    internal static readonly AsyncLocal<HttpClient?> TestFederatedHttpClient = new();

    /// <summary>
    /// Повторно запускает federated браузерный вход для существующего профиля
    /// и сохраняет новые токены.
    /// </summary>
    /// <param name="profileName">Имя профиля, для которого выполняется повторный вход.</param>
    /// <param name="launcher">Запускатель системного браузера.</param>
    /// <param name="ui">Интерактивный UI (для отображения фаз flow через статус-спиннер).</param>
    /// <param name="http"><see cref="HttpClient"/> для обмена <c>code → token</c>.</param>
    /// <param name="wireSink">Optional wire-log sink для захвата authorize URL.</param>
    /// <param name="timeout">Таймаут ожидания callback от браузера.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Полученный <see cref="FederatedTokenResult"/> (access + optional refresh + expiry).</returns>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.InvalidArgs"/> — профиль не существует, не является federated,
    /// либо у него отсутствует <c>federation_id</c>/<c>dpop_key_path</c>;
    /// <see cref="ErrorCode.AuthFailed"/> — ошибки PKCE/обмена <c>code → token</c>;
    /// <see cref="ErrorCode.ConfigError"/> — новые токены получены, но сохранить их не
    /// удалось: отказ файловой записи либо профиль удалили или подменили параллельным запуском
    /// <c>yt</c> за время браузерного флоу. Код здесь один на всю ситуацию «сессия выдана,
    /// но потеряна», чтобы вызывающий скрипт не разбирал два кода для одного исхода.
    /// </exception>
    public static async Task<FederatedTokenResult> ReloginAsync(
        string profileName,
        IBrowserLauncher launcher,
        IInteractiveUI ui,
        HttpClient http,
        IWireLogSink? wireSink,
        TimeSpan timeout,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(http);

        var store = new ConfigStore(ConfigStore.DefaultPath);
        var cfg = await store.LoadAsync(ct);

        if (!cfg.Profiles.TryGetValue(profileName, out var profile))
        {
            throw new TrackerException(
                ErrorCode.InvalidArgs,
                $"Profile '{profileName}' was not found in the configuration.");
        }

        var auth = profile.Auth;
        if (auth.Type != AuthType.Federated)
        {
            throw new TrackerException(
                ErrorCode.InvalidArgs,
                $"Profile '{profileName}' is not a federated profile (type={auth.Type}). "
                + "Re-login is only available for --type federated profiles.");
        }

        if (string.IsNullOrWhiteSpace(auth.FederationId))
        {
            throw new TrackerException(
                ErrorCode.InvalidArgs,
                $"Federated profile '{profileName}' is missing federation_id; cannot re-login.");
        }

        // Reuse the existing DPoP key (or materialize one at the recorded path). The same
        // key thumbprint is sent as `dpop_jkt`, so the server re-binds the new tokens to it.
        var keyPath = string.IsNullOrWhiteSpace(auth.DpopKeyPath)
            ? DPoPKeyStore.DefaultPathForProfile(profileName)
            : auth.DpopKeyPath!;
        var keyStore = new DPoPKeyStore(keyPath);
        using var dpopKey = keyStore.LoadOrCreate();

        var result = await ui.Status("Federated SSO re-login", async statusCtx =>
        {
            var phaseReporter = new SyncProgress<FederatedPhase>(p =>
            {
                var label = p.Kind switch
                {
                    FederatedPhaseKind.StartingCallbackServer => "Starting local callback server...",
                    FederatedPhaseKind.OpeningBrowser         => "Opening browser for sign-in...",
                    FederatedPhaseKind.WaitingForCallback     => $"Waiting for browser callback on :{p.CallbackPort}...",
                    FederatedPhaseKind.ExchangingCode         => "Exchanging code for token (DPoP)...",
                    FederatedPhaseKind.Completed              => "Saving profile...",
                    _ => p.Message,
                };
                statusCtx.Update(label);
            });

            return await FederatedOAuthFlow.RunAsync(
                auth.FederationId!,
                Commands.Auth.AuthLoginCommand.FederatedDefaultClientId,
                dpopKey,
                launcher,
                http,
                timeout,
                ct,
                wireLogSink: wireSink,
                phaseReporter: phaseReporter);
        }, ct);

        var expiresAtIso = result.ExpiresAt.ToUniversalTime().ToString("O");

        // Persist ONLY this profile's auth; everything else (other profiles, default_profile,
        // org settings, read_only, allowed_queues, allowed_write_issues, external_effects,
        // default_format) is
        // preserved verbatim. The `with`-expression is load-bearing: a positional constructor
        // call would silently drop any profile field added later, and for the policy fields
        // that means a restricted profile losing its restrictions mid-session — re-login also
        // happens automatically when a DPoP refresh fails.
        var newAuth = new AuthConfig(
            AuthType.Federated,
            Token: result.AccessToken,
            RefreshToken: result.RefreshToken,
            FederationId: auth.FederationId,
            DpopKeyPath: keyPath,
            AccessTokenExpiresAt: expiresAtIso);

        // The snapshot read above is minutes old by now — the browser flow ran in between —
        // so it must not reach the disk. ModifyAsync re-reads the file under the lock and the
        // update is applied to that fresh copy; anything another `yt` wrote meanwhile (a
        // tightened policy, a switched default profile, another profile's tokens) survives.
        // The lock covers only this read-modify-write: holding it across the browser flow
        // would block every other `yt` invocation for the duration of the login.
        try
        {
            await store.ModifyAsync(
                fresh =>
                {
                    if (!fresh.Profiles.TryGetValue(profileName, out var current))
                    {
                        // ConfigError по той же причине, что и у проверки идентичности ниже:
                        // профиль удалили параллельным запуском yt, аргументы команды тут ни
                        // при чём. Обе половины одной ситуации («сессия выдана, но сохранить
                        // её некуда») обязаны давать вызывающему один код.
                        throw new TrackerException(
                            ErrorCode.ConfigError,
                            $"Profile '{profileName}' disappeared from the configuration during re-login.");
                    }

                    // Идентичность профиля перепроверяется по свежему снимку, а не по тому,
                    // с которого начинался флоу. Если профиль за эти минуты пересоздали
                    // (`yt auth login --type oauth --profile <тот же>` из параллельной
                    // сессии), `current with { Auth = ... }` затёр бы свежие креденшелы
                    // федеративными — та же потеря обновления, только на креденшелах.
                    if (current.Auth.Type != AuthType.Federated
                        || !string.Equals(current.Auth.FederationId, auth.FederationId, StringComparison.Ordinal))
                    {
                        // ConfigError — по той же причине, что и у проверки выше.
                        throw new TrackerException(
                            ErrorCode.ConfigError,
                            $"Profile '{profileName}' changed during re-login "
                            + $"(expected a federated profile with federation_id='{auth.FederationId}', "
                            + $"found type={current.Auth.Type} with federation_id='{current.Auth.FederationId}').");
                    }

                    var profiles = new Dictionary<string, Profile>(fresh.Profiles)
                    {
                        [profileName] = current with { Auth = newAuth },
                    };
                    return new ConfigFile(fresh.DefaultProfile, profiles);
                },
                // Дошли сюда — значит сервер уже провернул токены: старый refresh мёртв,
                // новый есть только в памяти. Отказ по короткому общему таймауту здесь
                // выбросил бы только что выданную сессию.
                ConfigStore.PostAuthLockTimeout,
                ct);
        }
        catch (TrackerException ex) when (ex.Code == ErrorCode.ConfigError)
        {
            throw new TrackerException(
                ErrorCode.ConfigError,
                $"Re-login for profile '{profileName}' succeeded, but the new session could NOT be saved: "
                + ex.Message
                + " The tokens are lost and the ones left on disk are the expired ones that triggered this "
                + "re-login; the next attempt will need the browser flow again.",
                inner: ex);
        }

        return result;
    }
}
