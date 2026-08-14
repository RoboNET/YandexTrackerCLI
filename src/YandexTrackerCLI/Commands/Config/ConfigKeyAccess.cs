namespace YandexTrackerCLI.Commands.Config;

using Core.Api.Errors;
using YandexTrackerCLI.Core.Config;
using Output;

/// <summary>
/// Allowlist и мапперы ключей конфигурации для команд <c>yt config get/set</c>.
/// </summary>
/// <remarks>
/// Только перечисленные ключи доступны через CLI; попытка чтения/записи любого
/// другого ключа приводит к <see cref="ErrorCode.InvalidArgs"/>.
/// </remarks>
internal static class ConfigKeyAccess
{
    private static readonly string[] Allow =
    {
        "org_type", "org_id", "read_only", "default_format", "allowed_queues", "allowed_write_issues",
        "auth.type", "auth.token", "auth.service_account_id",
        "auth.key_id", "auth.private_key_path", "auth.private_key_pem",
    };

    /// <summary>
    /// Проверяет, что ключ разрешён allowlist'ом.
    /// </summary>
    /// <param name="key">Dotted-path ключа.</param>
    /// <exception cref="TrackerException">Если ключа нет в allowlist.</exception>
    public static void EnsureAllowed(string key)
    {
        if (Array.IndexOf(Allow, key) < 0)
        {
            throw new TrackerException(ErrorCode.InvalidArgs, $"Unknown config key: {key}");
        }
    }

    /// <summary>
    /// Проверяет, что значение ключа должно быть маскировано при выводе.
    /// </summary>
    /// <param name="key">Dotted-path ключа.</param>
    /// <returns><c>true</c> для <c>auth.token</c> и <c>auth.private_key_pem</c>.</returns>
    public static bool IsSecret(string key) =>
        key is "auth.token" or "auth.private_key_pem";

    /// <summary>
    /// Читает значение из профиля по dotted-path ключу.
    /// </summary>
    /// <param name="p">Профиль конфигурации.</param>
    /// <param name="key">Dotted-path ключа (должен быть в allowlist).</param>
    /// <returns>Строковое значение или <c>null</c>, если поле пустое.</returns>
    /// <exception cref="TrackerException">Если ключ не распознан.</exception>
    public static string? ReadValue(Profile p, string key) => key switch
    {
        "org_type"                => p.OrgType == OrgType.Cloud ? "cloud" : "yandex360",
        "org_id"                  => p.OrgId,
        "read_only"               => p.ReadOnly ? "true" : "false",
        "default_format"          => p.DefaultFormat,
        "allowed_queues"          => QueuePolicy.FormatList(QueuePolicy.Normalize(p.AllowedQueues)),
        "allowed_write_issues"    => IssueWritePolicy.FormatList(IssueWritePolicy.Normalize(p.AllowedWriteIssues)),
        "auth.type"               => p.Auth.Type switch
        {
            AuthType.OAuth          => "oauth",
            AuthType.IamStatic      => "iam-static",
            AuthType.ServiceAccount => "service-account",
            _                       => "unknown",
        },
        "auth.token"              => p.Auth.Token,
        "auth.service_account_id" => p.Auth.ServiceAccountId,
        "auth.key_id"             => p.Auth.KeyId,
        "auth.private_key_path"   => p.Auth.PrivateKeyPath,
        "auth.private_key_pem"    => p.Auth.PrivateKeyPem,
        _ => throw new TrackerException(ErrorCode.InvalidArgs, $"Unknown config key: {key}"),
    };

    /// <summary>
    /// Возвращает новый профиль с применённым изменением по dotted-path ключу.
    /// </summary>
    /// <param name="p">Исходный профиль.</param>
    /// <param name="key">Dotted-path ключа (должен быть в allowlist).</param>
    /// <param name="value">Новое значение.</param>
    /// <returns>Профиль с обновлённым полем.</returns>
    /// <exception cref="TrackerException">Если ключ не распознан или значение невалидно.</exception>
    public static Profile WriteValue(Profile p, string key, string value) => key switch
    {
        "org_type"                => p with { OrgType = ParseOrgType(value) },
        "org_id"                  => p with { OrgId = value },
        "read_only"               => p with { ReadOnly = ParseBool(value) },
        "default_format"          => p with { DefaultFormat = ValidateFormat(value) },
        "allowed_queues"          => p with { AllowedQueues = ParseAllowedQueues(value) },
        "allowed_write_issues"    => p with { AllowedWriteIssues = ParseAllowedWriteIssues(value) },
        "auth.type"               => p with { Auth = p.Auth with { Type = ParseAuthType(value) } },
        "auth.token"              => p with { Auth = p.Auth with { Token = value } },
        "auth.service_account_id" => p with { Auth = p.Auth with { ServiceAccountId = value } },
        "auth.key_id"             => p with { Auth = p.Auth with { KeyId = value } },
        "auth.private_key_path"   => p with { Auth = p.Auth with { PrivateKeyPath = value } },
        "auth.private_key_pem"    => p with { Auth = p.Auth with { PrivateKeyPem = value } },
        _ => throw new TrackerException(ErrorCode.InvalidArgs, $"Unknown config key: {key}"),
    };

    /// <summary>
    /// Разбирает значение <c>allowed_queues</c>: список ключей очередей через запятую.
    /// </summary>
    /// <param name="v">Строка вида <c>DEV,OPS,QA</c>; пустая строка снимает ограничение.</param>
    /// <returns>
    /// Нормализованный массив очередей, либо <c>null</c>, если после разбора не осталось
    /// ни одного элемента — тогда ключ вообще не пишется в конфиг и ограничение снято.
    /// </returns>
    private static string[]? ParseAllowedQueues(string v)
    {
        var parsed = QueuePolicy.ParseList(v);
        return parsed.Length == 0 ? null : parsed;
    }

    /// <summary>
    /// Разбирает значение <c>allowed_write_issues</c>: список ключей задач через запятую.
    /// </summary>
    /// <param name="v">Строка вида <c>DEV-42,DEV-43</c>; пустая строка снимает ограничение.</param>
    /// <returns>
    /// Нормализованный массив ключей, либо <c>null</c>, если после разбора не осталось
    /// ни одного элемента — тогда ключ вообще не пишется в конфиг и ограничение снято.
    /// </returns>
    private static string[]? ParseAllowedWriteIssues(string v)
    {
        var parsed = IssueWritePolicy.ParseList(v);
        return parsed.Length == 0 ? null : parsed;
    }

    private static string ValidateFormat(string v)
    {
        // Принимаем только конкретные форматы; "auto" не имеет смысла как сохраняемое
        // значение профиля (сам по себе профиль уже даёт детектируемый формат —
        // если в профиле default_format=auto, это эквивалентно отсутствию ключа).
        var parsed = FormatResolver.Parse(v, source: "default_format");
        if (parsed == OutputFormat.Auto)
        {
            throw new TrackerException(
                ErrorCode.InvalidArgs,
                "default_format must be one of: json|minimal|table.");
        }
        return parsed switch
        {
            OutputFormat.Json    => "json",
            OutputFormat.Minimal => "minimal",
            OutputFormat.Table   => "table",
            _ => throw new TrackerException(ErrorCode.InvalidArgs, $"Invalid format: '{v}'."),
        };
    }

    private static OrgType ParseOrgType(string v) => v switch
    {
        "yandex360" => OrgType.Yandex360,
        "cloud"     => OrgType.Cloud,
        _ => throw new TrackerException(
            ErrorCode.InvalidArgs,
            $"org_type must be yandex360 or cloud (was '{v}')."),
    };

    private static AuthType ParseAuthType(string v) => v switch
    {
        "oauth"           => AuthType.OAuth,
        "iam-static"      => AuthType.IamStatic,
        "service-account" => AuthType.ServiceAccount,
        _ => throw new TrackerException(
            ErrorCode.InvalidArgs,
            $"auth.type must be oauth|iam-static|service-account (was '{v}')."),
    };

    /// <remarks>
    /// Набор написаний тот же, что у ограничивающих env-переменных
    /// (<see cref="EnvBool.Classify"/>), — чтобы `read_only` в конфиге и <c>YT_READ_ONLY</c>
    /// понимали одно и то же. Код ошибки остаётся <see cref="ErrorCode.InvalidArgs"/>:
    /// здесь значение приходит аргументом команды, а не из окружения.
    /// </remarks>
    private static bool ParseBool(string v) => EnvBool.Classify(v) switch
    {
        EnvBoolValue.True  => true,
        EnvBoolValue.False => false,
        _ => throw new TrackerException(
            ErrorCode.InvalidArgs,
            $"read_only must be one of 1/true/yes/on or 0/false/no/off (was '{v}')."),
    };
}
