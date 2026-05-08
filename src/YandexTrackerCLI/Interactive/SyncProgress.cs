namespace YandexTrackerCLI.Interactive;

/// <summary>
/// Синхронный <see cref="IProgress{T}"/>: вызывает callback inline в потоке Report'а.
/// В отличие от <see cref="Progress{T}"/> не диспатчит через SyncContext/ThreadPool —
/// нужен для надёжного обновления Spectre live-region (Status/Progress) из background-таска.
/// </summary>
internal sealed class SyncProgress<T>(Action<T> callback) : IProgress<T>
{
    /// <summary>
    /// Сообщает значение, выполняя <paramref name="value"/>-callback inline в текущем потоке.
    /// </summary>
    /// <param name="value">Передаваемое значение прогресса.</param>
    public void Report(T value) => callback(value);
}
