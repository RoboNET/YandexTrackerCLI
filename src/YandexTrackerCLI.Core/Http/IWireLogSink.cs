namespace YandexTrackerCLI.Core.Http;

/// <summary>
/// Sink that receives serialized HTTP wire-log records produced by <see cref="WireLogHandler"/>.
/// </summary>
/// <remarks>
/// Implementations must be safe for concurrent invocations from multiple HTTP requests.
/// Records are passed as already-formatted text blocks (request, response, exception) and
/// must be appended verbatim, preserving line endings.
/// </remarks>
public interface IWireLogSink : IAsyncDisposable
{
    /// <summary>
    /// Appends a pre-formatted wire-log block to the sink.
    /// </summary>
    /// <param name="text">Pre-formatted block (already includes trailing newline).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the block has been written.</returns>
    ValueTask WriteAsync(string text, CancellationToken ct);
}
