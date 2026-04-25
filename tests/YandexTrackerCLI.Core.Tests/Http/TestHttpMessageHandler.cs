namespace YandexTrackerCLI.Core.Tests.Http;

internal sealed class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _handlers = new();

    public List<HttpRequestMessage> Seen { get; } = new();

    public TestHttpMessageHandler Push(Func<HttpRequestMessage, HttpResponseMessage> h)
    {
        _handlers.Enqueue(h);
        return this;
    }

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
