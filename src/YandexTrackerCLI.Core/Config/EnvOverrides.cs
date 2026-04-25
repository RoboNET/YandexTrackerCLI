namespace YandexTrackerCLI.Core.Config;

using Api.Errors;

public static class EnvOverrides
{
    public static EffectiveProfile Resolve(
        ConfigFile config,
        string? profileName,
        IReadOnlyDictionary<string, string?> env,
        bool cliReadOnly = false)
    {
        var name = profileName
                   ?? env.GetValueOrDefault("YT_PROFILE")
                   ?? config.DefaultProfile;

        Profile? baseProfile = null;
        if (config.Profiles.TryGetValue(name, out var p))
        {
            baseProfile = p;
        }

        var saId = Trimmed(env, "YT_SERVICE_ACCOUNT_ID");
        var keyId = Trimmed(env, "YT_SERVICE_ACCOUNT_KEY_ID");
        var keyFile = Trimmed(env, "YT_SERVICE_ACCOUNT_KEY_FILE");
        var keyPem = Trimmed(env, "YT_SERVICE_ACCOUNT_KEY_PEM");
        var iamToken = Trimmed(env, "YT_IAM_TOKEN");
        var oauthToken = Trimmed(env, "YT_OAUTH_TOKEN");

        AuthConfig? auth = null;

        var anySaField = saId is not null || keyId is not null || keyFile is not null || keyPem is not null;
        if (anySaField)
        {
            var missing = saId is null || keyId is null || (keyFile is null && keyPem is null);
            if (missing)
            {
                throw new TrackerException(ErrorCode.ConfigError,
                    "Partial service-account env: need YT_SERVICE_ACCOUNT_ID, YT_SERVICE_ACCOUNT_KEY_ID and either YT_SERVICE_ACCOUNT_KEY_FILE or YT_SERVICE_ACCOUNT_KEY_PEM.");
            }
            auth = new AuthConfig(AuthType.ServiceAccount,
                ServiceAccountId: saId,
                KeyId: keyId,
                PrivateKeyPath: keyFile,
                PrivateKeyPem: keyPem);
        }
        else if (iamToken is not null)
        {
            auth = new AuthConfig(AuthType.IamStatic, Token: iamToken);
        }
        else if (oauthToken is not null)
        {
            auth = new AuthConfig(AuthType.OAuth, Token: oauthToken);
        }
        else if (baseProfile is not null)
        {
            auth = baseProfile.Auth;
        }

        if (auth is null)
        {
            throw new TrackerException(ErrorCode.ConfigError,
                $"No auth configuration found for profile '{name}' (neither in config nor in env).");
        }

        var orgType = ParseOrgType(Trimmed(env, "YT_ORG_TYPE")) ?? baseProfile?.OrgType;
        var orgId = Trimmed(env, "YT_ORG_ID") ?? baseProfile?.OrgId;

        if (orgType is null || string.IsNullOrWhiteSpace(orgId))
        {
            throw new TrackerException(ErrorCode.ConfigError,
                "Organization not configured. Set YT_ORG_TYPE and YT_ORG_ID, or configure profile.");
        }

        var envRo = ParseBool(Trimmed(env, "YT_READ_ONLY"));
        var readOnly = cliReadOnly || envRo || (baseProfile?.ReadOnly ?? false);

        return new EffectiveProfile(
            name,
            orgType.Value,
            orgId,
            readOnly,
            auth,
            DefaultFormat: baseProfile?.DefaultFormat);
    }

    private static string? Trimmed(IReadOnlyDictionary<string, string?> env, string key)
    {
        var v = env.GetValueOrDefault(key);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static OrgType? ParseOrgType(string? value) => value switch
    {
        "yandex360" => OrgType.Yandex360,
        "cloud"     => OrgType.Cloud,
        null        => null,
        _ => throw new TrackerException(ErrorCode.ConfigError, $"Unknown YT_ORG_TYPE: '{value}'."),
    };

    private static bool ParseBool(string? value) =>
        value is "1" or "true" or "True" or "TRUE" or "yes";
}
