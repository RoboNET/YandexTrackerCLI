namespace YandexTrackerCLI.Core.Api;

using System.Net.Http;

/// <summary>
/// Represents a streaming download response from the Yandex Tracker API.
/// Owns both the underlying network stream and the <see cref="HttpResponseMessage"/>,
/// releasing them in order when disposed.
/// </summary>
public sealed class TrackerDownload : IAsyncDisposable
{
    private readonly HttpResponseMessage _response;
    private bool _disposed;

    /// <summary>
    /// Gets the response body stream. The stream is owned by this instance and must not be disposed directly.
    /// </summary>
    public Stream Stream { get; }

    /// <summary>
    /// Gets the value of the <c>Content-Length</c> response header, if present.
    /// </summary>
    public long? ContentLength { get; }

    /// <summary>
    /// Gets the filename parsed from the <c>Content-Disposition</c> response header, if present.
    /// Surrounding double quotes are trimmed.
    /// </summary>
    public string? FileName { get; }

    /// <summary>
    /// Gets the media type parsed from the <c>Content-Type</c> response header, if present.
    /// </summary>
    public string? ContentType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TrackerDownload"/> class.
    /// </summary>
    /// <param name="response">The owned HTTP response to release on disposal.</param>
    /// <param name="stream">The response body stream to release before the response.</param>
    internal TrackerDownload(HttpResponseMessage response, Stream stream)
    {
        _response = response;
        Stream = stream;
        ContentLength = response.Content.Headers.ContentLength;
        FileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        ContentType = response.Content.Headers.ContentType?.MediaType;
    }

    /// <summary>
    /// Disposes the response stream and then the underlying <see cref="HttpResponseMessage"/>.
    /// </summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await Stream.DisposeAsync();
        _response.Dispose();
    }
}
