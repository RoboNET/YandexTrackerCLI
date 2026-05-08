namespace YandexTrackerCLI.Interactive;

/// <summary>
/// No-op реализация <see cref="IInteractiveUI"/>: исполняет переданную работу без
/// какого-либо вывода. Используется в JSON/Minimal-режимах и при перенаправлении
/// stdout/stderr — гарантирует bit-exact pipe-output.
/// </summary>
public sealed class NoopInteractiveUI : IInteractiveUI
{
    /// <summary>
    /// Глобальный singleton no-op UI: stateless, потокобезопасен.
    /// </summary>
    public static readonly NoopInteractiveUI Instance = new();

    private static readonly IStatusContext NoopContext = new NoopStatusContext();

    private NoopInteractiveUI() { }

    /// <inheritdoc />
    public bool IsRich => false;

    /// <inheritdoc />
    public Task<T> Status<T>(string label, Func<IStatusContext, Task<T>> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return work(NoopContext);
    }

    /// <inheritdoc />
    public Task Status(string label, Func<IStatusContext, Task> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return work(NoopContext);
    }

    private sealed class NoopStatusContext : IStatusContext
    {
        public void Update(string label) { }
        public void Spinner(SpinnerStyle style) { }
    }
}
