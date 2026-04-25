namespace YandexTrackerCLI.Output;

using System.Globalization;
using Core.Api.Errors;

/// <summary>
/// Резолвер эффективного формата вывода: выбирает один из конкретных
/// <see cref="OutputFormat"/> (никогда не возвращает <see cref="OutputFormat.Auto"/>)
/// на основе cascade — CLI флаг → env <c>YT_FORMAT</c> → профиль <c>default_format</c> →
/// auto-detect по TTY.
/// </summary>
public static class FormatResolver
{
    /// <summary>
    /// Имя environment-переменной, через которую можно задать формат вывода глобально
    /// (например, <c>YT_FORMAT=table</c>).
    /// </summary>
    public const string EnvVarName = "YT_FORMAT";

    /// <summary>
    /// Резолвит эффективный формат вывода по правилам cascade:
    /// 1) <paramref name="cliFormat"/>, если он не <see cref="OutputFormat.Auto"/>;
    /// 2) env <c>YT_FORMAT</c>, если задан и валиден;
    /// 3) <paramref name="profileDefaultFormat"/>, если задан и валиден;
    /// 4) <paramref name="isOutputRedirected"/>: <c>true</c> → <see cref="OutputFormat.Json"/>,
    ///    <c>false</c> → <see cref="OutputFormat.Table"/>.
    /// </summary>
    /// <param name="cliFormat">Значение, полученное от <c>--format</c> флага CLI.</param>
    /// <param name="env">Snapshot переменных окружения (используется ключ <c>YT_FORMAT</c>).</param>
    /// <param name="profileDefaultFormat">Значение из профиля (<c>default_format</c>); может быть <c>null</c>.</param>
    /// <param name="isOutputRedirected">Признак того, что stdout перенаправлен (pipe/file).</param>
    /// <returns>Один из <see cref="OutputFormat.Json"/>, <see cref="OutputFormat.Minimal"/>, <see cref="OutputFormat.Table"/>.</returns>
    /// <exception cref="TrackerException">Если env <c>YT_FORMAT</c> или <paramref name="profileDefaultFormat"/> содержат невалидное значение.</exception>
    public static OutputFormat Resolve(
        OutputFormat cliFormat,
        IReadOnlyDictionary<string, string?> env,
        string? profileDefaultFormat,
        bool isOutputRedirected)
    {
        if (cliFormat != OutputFormat.Auto)
        {
            return cliFormat;
        }

        if (env.TryGetValue(EnvVarName, out var envValue) && !string.IsNullOrWhiteSpace(envValue))
        {
            var fromEnv = Parse(envValue, source: EnvVarName);
            if (fromEnv != OutputFormat.Auto)
            {
                return fromEnv;
            }
            // env=auto — продолжаем cascade.
        }

        if (!string.IsNullOrWhiteSpace(profileDefaultFormat))
        {
            var fromProfile = Parse(profileDefaultFormat, source: "profile.default_format");
            if (fromProfile != OutputFormat.Auto)
            {
                return fromProfile;
            }
            // profile=auto — продолжаем cascade.
        }

        return isOutputRedirected ? OutputFormat.Json : OutputFormat.Table;
    }

    /// <summary>
    /// Парсит строковое значение в <see cref="OutputFormat"/> case-insensitive.
    /// Допустимы: <c>json</c>, <c>minimal</c>, <c>table</c>, <c>auto</c>.
    /// </summary>
    /// <param name="value">Строковое представление формата.</param>
    /// <param name="source">Источник значения (для текста ошибки), например <c>YT_FORMAT</c>.</param>
    /// <returns>Распарсенный <see cref="OutputFormat"/>.</returns>
    /// <exception cref="TrackerException">Если значение не распознано.</exception>
    public static OutputFormat Parse(string value, string source)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "json"    => OutputFormat.Json,
            "minimal" => OutputFormat.Minimal,
            "table"   => OutputFormat.Table,
            "auto"    => OutputFormat.Auto,
            _ => throw new TrackerException(
                ErrorCode.InvalidArgs,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Invalid format value '{0}' (from {1}). Expected: json|minimal|table|auto.",
                    value,
                    source)),
        };
    }
}
