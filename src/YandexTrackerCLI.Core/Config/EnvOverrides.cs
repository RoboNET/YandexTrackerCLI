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
        var explicitProfile = profileName ?? env.GetValueOrDefault("YT_PROFILE");
        var name = explicitProfile ?? config.DefaultProfile;

        Profile? baseProfile = null;
        if (!string.IsNullOrEmpty(name) && config.Profiles.TryGetValue(name, out var p))
        {
            baseProfile = p;
        }
        else if (explicitProfile is null && config.Profiles.Count == 1)
        {
            // Auto-default: no explicit selection and no valid default_profile,
            // but exactly one profile is configured — use it.
            var only = config.Profiles.First();
            name = only.Key;
            baseProfile = only.Value;
        }
        else if (explicitProfile is null && config.Profiles.Count > 1)
        {
            // Ambiguous: multiple profiles configured but no default selected and
            // no valid default_profile. Force the user to choose explicitly.
            throw new TrackerException(ErrorCode.ConfigError,
                "No default profile selected and multiple profiles are configured " +
                $"({string.Join(", ", config.Profiles.Keys)}). " +
                "Run `yt config profile <name>` to set a default, or pass `--profile <name>`.");
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

        // Разбор строгий (см. EnvBool): нераспознанное значение — ошибка, а не «выключено».
        // Единственная задача переменной — ограничивать, и молча проигнорировать `on` или
        // опечатку значило бы оставить без защиты того, кто считает себя защищённым.
        var envRo = EnvBool.ResolveStrict(env, "YT_READ_ONLY", "the profile's read_only");
        var readOnly = cliReadOnly || envRo || (baseProfile?.ReadOnly ?? false);

        // allowed_queues намеренно не имеет env-override: это свойство самих креденшелов,
        // а переменная окружения так же управляема вызывающим, как и флаг командной строки.
        var allowedQueues = QueuePolicy.Normalize(baseProfile?.AllowedQueues);

        // allowed_write_issues, в отличие от allowed_queues, env-override имеет: в CI задача
        // каждый раз своя, и список приходится задавать на запуск. Но окружение управляемо
        // тем же вызывающим, что и командная строка, поэтому переменная может область записи
        // только СУЗИТЬ: профиль без списка + env = список из env, профиль со списком + env =
        // их пересечение. Расширения не даёт ни одно сочетание значений.
        var profileWriteIssues = IssueWritePolicy.Normalize(baseProfile?.AllowedWriteIssues);
        var envWriteIssues = Trimmed(env, "YT_ALLOWED_WRITE_ISSUES");
        string[] allowedWriteIssues;
        WriteIssuesSource writeIssuesSource;
        if (envWriteIssues is null)
        {
            allowedWriteIssues = profileWriteIssues;
            writeIssuesSource = WriteIssuesSource.Profile;
        }
        else
        {
            var envList = IssueWritePolicy.ParseList(envWriteIssues);
            if (envList.Length == 0)
            {
                // Значение задано (непустое после Trim), но ни одного ключа из него не
                // получилось — например "," или ",,". Молча трактовать это как «ограничения
                // нет» нельзя: так опечатка в разделителях снимала бы политику профиля.
                throw new TrackerException(
                    ErrorCode.ConfigError,
                    $"YT_ALLOWED_WRITE_ISSUES is set to '{envWriteIssues}', which contains no issue keys. "
                    + "Pass a comma-separated list (e.g. 'DEV-42,DEV-43'), or unset the variable "
                    + "to fall back to the profile's allowed_write_issues.");
            }

            if (profileWriteIssues.Length == 0)
            {
                allowedWriteIssues = envList;
                writeIssuesSource = WriteIssuesSource.Env;
            }
            else
            {
                var intersection = IssueWritePolicy.Intersect(profileWriteIssues, envList);
                if (intersection.Length == 0)
                {
                    // Пустое пересечение — не «ограничения нет»: пустой список во всей модели
                    // означает ровно отсутствие ограничения, и вернуть его здесь значило бы
                    // открыть запись всюду. Отдельного состояния «писать нельзя никуда» в
                    // модели нет намеренно: его пришлось бы протаскивать через каждую точку
                    // проверки, и любая пропущенная превратилась бы в дыру. Поэтому резолв
                    // падает целиком — команда не выполняется вовсе, а оператор видит, какие
                    // два списка не сошлись.
                    throw new TrackerException(
                        ErrorCode.ConfigError,
                        $"YT_ALLOWED_WRITE_ISSUES ({string.Join(", ", envList)}) does not intersect "
                        + $"allowed_write_issues of profile '{name}' ({string.Join(", ", profileWriteIssues)}). "
                        + "The variable can only narrow the profile's write scope, never extend it: "
                        + "pass issues from the profile's list, or unset the variable to use that list as is.");
                }

                allowedWriteIssues = intersection;
                writeIssuesSource = WriteIssuesSource.ProfileAndEnv;
            }
        }

        return new EffectiveProfile(
            name,
            orgType.Value,
            orgId,
            readOnly,
            auth,
            DefaultFormat: baseProfile?.DefaultFormat,
            AllowedQueues: allowedQueues,
            AllowedWriteIssues: allowedWriteIssues,
            AllowedWriteIssuesSource: writeIssuesSource);
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
}
