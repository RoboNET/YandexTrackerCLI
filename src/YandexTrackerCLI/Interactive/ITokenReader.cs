namespace YandexTrackerCLI.Interactive;

/// <summary>
/// Абстракция для чтения OAuth-токена со stdin / из интерактивного терминала.
/// </summary>
/// <remarks>
/// Позволяет тестам подменять <see cref="Console"/>-based ввод без <c>Console.SetIn</c>
/// и отличать TTY от piped stdin (важно для отказа от интерактивного flow
/// в non-TTY окружении).
/// </remarks>
public interface ITokenReader
{
    /// <summary>
    /// <c>true</c>, если stdin перенаправлен (не TTY); в этом случае
    /// интерактивный flow должен быть отключён.
    /// </summary>
    bool IsInputRedirected { get; }

    /// <summary>
    /// Читает одну строку ввода (без завершающего перевода строки).
    /// </summary>
    /// <returns>Строка ввода или <c>null</c> при достижении EOF.</returns>
    string? ReadLine();
}
