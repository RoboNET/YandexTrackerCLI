namespace YandexTrackerCLI.Core.Tests.Api;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Core.Api;
using Http;

public sealed class TrackerClientCursorPagingTests
{
    [Test]
    public async Task GetCursorPagedAsync_NonAdvancingCursor_TerminatesWithoutInfiniteLoop()
    {
        // Server keeps returning a "next" link pointing back at the very same request URI.
        // Without a guard this would spin forever; the enumeration must terminate after one page.
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"id\":\"1\"}]", Encoding.UTF8, "application/json"),
            };
            // Absolute self-referencing next link (same path + query as the first request).
            r.Headers.TryAddWithoutValidation(
                "Link",
                "<https://api.tracker.yandex.net/v3/issues/DEV-1/changelog?perPage=50>; rel=\"next\"");
            return r;
        });
        using var http = new HttpClient(inner) { BaseAddress = new Uri("https://api.tracker.yandex.net/v3/") };
        var client = new TrackerClient(http);

        var items = new List<JsonElement>();
        await foreach (var item in client.GetCursorPagedAsync("issues/DEV-1/changelog", perPage: 50))
        {
            items.Add(item);
        }

        // Exactly one HTTP request was issued and the loop stopped instead of looping forever.
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        await Assert.That(items.Count).IsEqualTo(1);
    }
}
