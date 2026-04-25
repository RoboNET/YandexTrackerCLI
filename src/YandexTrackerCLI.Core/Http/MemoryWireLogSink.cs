namespace YandexTrackerCLI.Core.Http;

/// <summary>
/// In-memory implementation of <see cref="IWireLogSink"/> used by tests to capture
/// formatted wire-log records for assertions.
/// </summary>
internal sealed class MemoryWireLogSink : IWireLogSink
{
    private readonly Lock _gate = new();
    private readonly List<string> _records = new();

    /// <summary>
    /// Gets a snapshot copy of all records captured so far.
    /// </summary>
    public IReadOnlyList<string> Records
    {
        get
        {
            lock (_gate)
            {
                return _records.ToArray();
            }
        }
    }

    /// <summary>
    /// Returns all captured records concatenated into a single string.
    /// </summary>
    public string Joined
    {
        get
        {
            lock (_gate)
            {
                return string.Concat(_records);
            }
        }
    }

    /// <inheritdoc />
    public ValueTask WriteAsync(string text, CancellationToken ct)
    {
        lock (_gate)
        {
            _records.Add(text);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
