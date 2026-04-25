namespace YandexTrackerCLI.Interactive;

/// <summary>
/// Реализация <see cref="ITokenReader"/>, делегирующая чтение <see cref="Console"/>.
/// </summary>
/// <remarks>
/// Используется как default в <see cref="Commands.Auth.AuthLoginCommand"/>.
/// Тесты подменяют её фейковой реализацией через AsyncLocal-override.
/// </remarks>
public sealed class ConsoleTokenReader : ITokenReader
{
    /// <inheritdoc />
    public bool IsInputRedirected => Console.IsInputRedirected;

    /// <inheritdoc />
    public string? ReadLine() => Console.ReadLine();
}
