namespace YandexTrackerCLI.Interactive;

/// <summary>
/// Абстракция для открытия URL в системном браузере.
/// </summary>
/// <remarks>
/// Реализации предоставляют кросс-платформенный запуск ("open" на macOS,
/// "xdg-open" на Linux, "cmd /c start" на Windows). Инжектируется в
/// <see cref="Commands.Auth.AuthLoginCommand"/> для interactive OAuth flow.
/// Тесты используют фейковые реализации, чтобы не зависеть от реального
/// окружения (headless CI может не иметь браузера).
/// </remarks>
public interface IBrowserLauncher
{
    /// <summary>
    /// Открывает указанный URL в браузере пользователя.
    /// </summary>
    /// <param name="url">Целевой URL (например, https://oauth.yandex.ru/authorize?...).</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Задача, завершающаяся сразу после запуска процесса.</returns>
    /// <exception cref="System.PlatformNotSupportedException">
    /// Бросается, когда текущая ОС не поддерживается реализацией.
    /// </exception>
    Task OpenAsync(string url, CancellationToken ct);
}
