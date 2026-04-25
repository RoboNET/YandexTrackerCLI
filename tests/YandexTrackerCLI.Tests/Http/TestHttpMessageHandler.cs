namespace YandexTrackerCLI.Tests.Http;

/// <summary>
/// Deterministic <see cref="HttpMessageHandler"/> for unit tests: records every seen
/// <see cref="HttpRequestMessage"/> and returns responses from a FIFO queue of handlers.
/// </summary>
internal sealed class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _handlers = new();

    /// <summary>
    /// Gets the list of requests seen by this handler, in the order they arrived.
    /// </summary>
    public List<HttpRequestMessage> Seen { get; } = new();

    /// <summary>
    /// Enqueues a response factory to be used for the next incoming request.
    /// </summary>
    /// <param name="h">The response factory.</param>
    /// <returns>This handler, for chaining.</returns>
    public TestHttpMessageHandler Push(Func<HttpRequestMessage, HttpResponseMessage> h)
    {
        _handlers.Enqueue(h);
        return this;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Seen.Add(request);
        if (_handlers.Count == 0)
        {
            throw new InvalidOperationException("No queued handler for request " + request.Method + " " + request.RequestUri);
        }

        return Task.FromResult(_handlers.Dequeue()(request));
    }
}
