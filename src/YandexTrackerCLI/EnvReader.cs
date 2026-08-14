namespace YandexTrackerCLI;

using YandexTrackerCLI.Core.Config;

/// <summary>
/// Читает (snapshot) переменные окружения, которые используются CLI,
/// и возвращает неизменяемую копию для передачи в <c>EnvOverrides.Resolve</c>.
/// </summary>
public static class EnvReader
{
    private static readonly string[] Keys =
    {
        "YT_PROFILE", "YT_OAUTH_TOKEN", "YT_IAM_TOKEN",
        "YT_SERVICE_ACCOUNT_ID", "YT_SERVICE_ACCOUNT_KEY_ID",
        "YT_SERVICE_ACCOUNT_KEY_FILE", "YT_SERVICE_ACCOUNT_KEY_PEM",
        "YT_ORG_TYPE", "YT_ORG_ID", "YT_READ_ONLY", "YT_ALLOWED_WRITE_ISSUES", "YT_CONFIG_PATH",
        "YT_API_BASE_URL", "YT_TIMEOUT",
        "YT_FORMAT",
        // Wire-log: читаются в TrackerContextFactory для ЛЮБОЙ команды. Пока их тут не было,
        // TryGetValue всегда возвращал false, и env-переменные работали только у auth-команд,
        // которые лезут в Environment напрямую.
        "YT_LOG_FILE", "YT_LOG_RAW",
        // Terminal capabilities (markdown rendering, hyperlinks, pager).
        "YT_PAGER", "YT_HYPERLINKS", "YT_TERMINAL_WIDTH",
        // Цвет выключают NO_COLOR (no-color.org), --no-color, TERM=dumb и редирект stdout.
        // Собственной YT_NO_COLOR намеренно нет: она ничего не добавляет к стандартной
        // переменной, а вторая переменная с тем же смыслом — только повод спутать их.
        "NO_COLOR", "TERM", "TERM_PROGRAM", "COLORTERM", "PAGER",
    };

    /// <summary>
    /// Создаёт snapshot значений известных переменных окружения.
    /// </summary>
    /// <returns>Словарь <c>имя → значение</c> (значение может быть <c>null</c>).</returns>
    public static IReadOnlyDictionary<string, string?> Snapshot()
    {
        var d = new Dictionary<string, string?>(Keys.Length);
        foreach (var k in Keys)
        {
            d[k] = Environment.GetEnvironmentVariable(k);
        }
        return d;
    }

    /// <summary>
    /// Разбирает <c>YT_LOG_RAW</c> — единственную точку решения «снимать ли маскирование
    /// секретов в wire-log». Разбор строгий: raw-режим включает только явное истинное
    /// написание, любое нераспознанное значение — ошибка конфигурации, а не «включено».
    /// </summary>
    /// <param name="raw">Сырое значение переменной (может быть <see langword="null"/>).</param>
    /// <returns><see langword="true"/>, если маскирование нужно снять.</returns>
    /// <exception cref="Core.Api.Errors.TrackerException">
    /// <see cref="Core.Api.Errors.ErrorCode.ConfigError"/> — значение непустое и не булево.
    /// </exception>
    public static bool ResolveLogRaw(string? raw) =>
        EnvBool.ResolveStrict(
            raw,
            "YT_LOG_RAW",
            "the default masked wire log (secrets replaced with ***)");

    /// <summary>
    /// Разбирает <c>YT_LOG_RAW</c> из snapshot'а окружения.
    /// </summary>
    /// <param name="env">Snapshot переменных окружения.</param>
    /// <returns><see langword="true"/>, если маскирование нужно снять.</returns>
    /// <exception cref="Core.Api.Errors.TrackerException">
    /// <see cref="Core.Api.Errors.ErrorCode.ConfigError"/> — значение непустое и не булево.
    /// </exception>
    public static bool ResolveLogRaw(IReadOnlyDictionary<string, string?> env) =>
        ResolveLogRaw(env.GetValueOrDefault("YT_LOG_RAW"));
}
