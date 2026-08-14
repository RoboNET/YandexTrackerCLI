namespace YandexTrackerCLI;

using Core.Api.Errors;
using Core.Http;

/// <summary>
/// Единственное место, где резолвится эффективный HTTP-таймаут CLI:
/// <c>--timeout</c> → env <c>YT_TIMEOUT</c> → дефолт
/// (<see cref="TrackerHttpClientFactory.DefaultTimeout"/>).
/// </summary>
/// <remarks>
/// Каскад нужен в двух разных местах — при построении HTTP-клиента
/// (<see cref="TrackerContextFactory"/>) и при формировании текста ошибки отмены
/// (<see cref="CliRunner"/>). Пока их было два, они могли разойтись: сообщение
/// называло пользователю «текущее значение», которого фабрика никогда не применяла.
/// Здесь же живёт и проверка допустимости: <c>0</c> и отрицательные значения
/// <see cref="HttpClient"/> не принимает, поэтому они отвергаются явной ошибкой,
/// а не превращаются в <see cref="ArgumentOutOfRangeException"/> из глубины конвейера.
/// </remarks>
internal static class TimeoutResolver
{
    /// <summary>
    /// Таймаут по умолчанию в секундах.
    /// </summary>
    public static int DefaultSeconds => (int)TrackerHttpClientFactory.DefaultTimeout.TotalSeconds;

    /// <summary>
    /// Максимально допустимое значение таймаута в секундах (сутки). Всё, что больше,
    /// на практике означает опечатку и молча превращает CLI в вечно висящий процесс.
    /// </summary>
    public const int MaxSeconds = 86_400;

    /// <summary>
    /// Резолвит эффективный таймаут и проверяет его допустимость.
    /// </summary>
    /// <param name="cliSeconds">Значение <c>--timeout</c> либо <see langword="null"/>.</param>
    /// <param name="env">Snapshot переменных окружения (<see cref="EnvReader.Snapshot"/>).</param>
    /// <returns>Эффективный таймаут в секундах.</returns>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.InvalidArgs"/> — значение не является положительным целым
    /// в допустимом диапазоне.
    /// </exception>
    public static int Resolve(int? cliSeconds, IReadOnlyDictionary<string, string?> env)
    {
        if (cliSeconds is { } cli)
        {
            if (!IsValid(cli))
            {
                throw new TrackerException(
                    ErrorCode.InvalidArgs,
                    $"--timeout must be between 1 and {MaxSeconds} seconds, got {cli}");
            }

            return cli;
        }

        if (env.TryGetValue("YT_TIMEOUT", out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            if (!int.TryParse(raw, out var parsed) || !IsValid(parsed))
            {
                throw new TrackerException(
                    ErrorCode.InvalidArgs,
                    $"YT_TIMEOUT must be an integer between 1 and {MaxSeconds} seconds, got \"{raw}\"");
            }

            return parsed;
        }

        return DefaultSeconds;
    }

    /// <summary>
    /// Резолвит таймаут для текста сообщения: недопустимое значение не называется как
    /// «текущее», а заменяется дефолтом.
    /// </summary>
    /// <param name="cliSeconds">Значение <c>--timeout</c> либо <see langword="null"/>.</param>
    /// <param name="env">Snapshot переменных окружения.</param>
    /// <returns>Эффективный таймаут в секундах.</returns>
    /// <remarks>
    /// Текст сообщения — инструкция пользователю, и врать в нём нельзя: если
    /// <c>--timeout 0</c> или <c>YT_TIMEOUT=-5</c> отвергнуты, то работал дефолт,
    /// его и следует называть.
    /// </remarks>
    public static int ResolveForMessage(int? cliSeconds, IReadOnlyDictionary<string, string?> env)
    {
        try
        {
            return Resolve(cliSeconds, env);
        }
        catch (TrackerException)
        {
            return DefaultSeconds;
        }
    }

    private static bool IsValid(int seconds) => seconds is > 0 and <= MaxSeconds;
}
