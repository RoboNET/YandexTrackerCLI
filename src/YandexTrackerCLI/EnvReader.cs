namespace YandexTrackerCLI;

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
        "YT_ORG_TYPE", "YT_ORG_ID", "YT_READ_ONLY", "YT_CONFIG_PATH",
        "YT_API_BASE_URL", "YT_TIMEOUT", "YT_NO_COLOR",
        "YT_FORMAT",
        // Terminal capabilities (markdown rendering, hyperlinks, pager).
        "YT_PAGER", "YT_HYPERLINKS", "YT_TERMINAL_WIDTH",
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
}
