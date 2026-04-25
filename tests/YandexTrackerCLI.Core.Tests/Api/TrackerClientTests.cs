namespace YandexTrackerCLI.Core.Tests.Api;

using System.Net;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Core.Api;
using YandexTrackerCLI.Core.Api.Errors;
using Http;

public sealed class TrackerClientTests
{
    private static HttpClient MakeClient(TestHttpMessageHandler inner) =>
        new(inner) { BaseAddress = new Uri("https://api.tracker.yandex.net/v3/") };

    [Test]
    public async Task GetAsync_200_ReturnsJsonElement()
    {
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"id":1,"nested":{"k":"v"}}""", Encoding.UTF8, "application/json");
            return r;
        });
        using var http = MakeClient(inner);
        var client = new TrackerClient(http);

        var result = await client.GetAsync("myself");

        await Assert.That(result.GetProperty("id").GetInt32()).IsEqualTo(1);
        await Assert.That(result.GetProperty("nested").GetProperty("k").GetString()).IsEqualTo("v");
    }

    [Test]
    [Arguments(HttpStatusCode.NotFound, ErrorCode.NotFound)]
    [Arguments(HttpStatusCode.Unauthorized, ErrorCode.AuthFailed)]
    [Arguments(HttpStatusCode.Forbidden, ErrorCode.Forbidden)]
    [Arguments(HttpStatusCode.TooManyRequests, ErrorCode.RateLimited)]
    [Arguments((HttpStatusCode)500, ErrorCode.ServerError)]
    [Arguments((HttpStatusCode)502, ErrorCode.ServerError)]
    [Arguments((HttpStatusCode)503, ErrorCode.ServerError)]
    public async Task GetAsync_ErrorStatus_ThrowsWithMappedCode(HttpStatusCode status, ErrorCode expected)
    {
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(status));
        using var http = MakeClient(inner);
        var client = new TrackerClient(http);

        var ex = await Assert.ThrowsAsync<TrackerException>(() => client.GetAsync("myself"));
        await Assert.That(ex!.Code).IsEqualTo(expected);
        await Assert.That(ex.HttpStatus).IsEqualTo((int)status);
    }

    [Test]
    public async Task GetAsync_IncludesTraceId_WhenHeaderPresent()
    {
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.InternalServerError);
            r.Headers.TryAddWithoutValidation("X-Trace-Id", "abc-123");
            return r;
        });
        using var http = MakeClient(inner);
        var client = new TrackerClient(http);

        var ex = await Assert.ThrowsAsync<TrackerException>(() => client.GetAsync("myself"));
        await Assert.That(ex!.TraceId).IsEqualTo("abc-123");
    }

    [Test]
    public async Task PostJsonAsync_SendsBodyAsJson_ReturnsResponse()
    {
        string? capturedBody = null;
        string? capturedMediaType = null;
        var inner = new TestHttpMessageHandler();
        inner.Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedMediaType = req.Content!.Headers.ContentType!.MediaType;
            var r = new HttpResponseMessage(HttpStatusCode.Created);
            r.Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json");
            return r;
        });
        using var http = MakeClient(inner);
        var client = new TrackerClient(http);

        using var doc = JsonDocument.Parse("""{"summary":"test"}""");
        var result = await client.PostJsonAsync("issues", doc.RootElement);

        await Assert.That(result.GetProperty("ok").GetBoolean()).IsTrue();
        await Assert.That(capturedBody).IsEqualTo("""{"summary":"test"}""");
        await Assert.That(capturedMediaType).IsEqualTo("application/json");
    }

    [Test]
    public async Task GetPagedAsync_ConcatenatesPages_UsingXTotalPages()
    {
        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "2");
            r.Content = new StringContent("""[{"k":"A"},{"k":"B"}]""", Encoding.UTF8, "application/json");
            return r;
        });
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Headers.Add("X-Total-Pages", "2");
            r.Content = new StringContent("""[{"k":"C"}]""", Encoding.UTF8, "application/json");
            return r;
        });
        using var http = MakeClient(inner);
        var client = new TrackerClient(http);

        var collected = new List<string>();
        await foreach (var el in client.GetPagedAsync("queues"))
        {
            collected.Add(el.GetProperty("k").GetString()!);
        }

        await Assert.That(collected).IsEquivalentTo(new[] { "A", "B", "C" });
        await Assert.That(inner.Seen.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetPagedAsync_SingleObjectResponse_YieldedOnce()
    {
        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"k":"single"}""", Encoding.UTF8, "application/json");
            return r;
        });
        using var http = MakeClient(inner);
        var client = new TrackerClient(http);

        var collected = new List<string>();
        await foreach (var el in client.GetPagedAsync("something"))
        {
            collected.Add(el.GetProperty("k").GetString()!);
        }

        await Assert.That(collected).IsEquivalentTo(new[] { "single" });
    }

    [Test]
    public async Task GetPagedAsync_NoXTotalPages_YieldsSinglePage_ThenStops()
    {
        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""[{"k":"A"}]""", Encoding.UTF8, "application/json");
            return r;
        });
        using var http = MakeClient(inner);
        var client = new TrackerClient(http);

        var keys = new List<string>();
        await foreach (var el in client.GetPagedAsync("items"))
        {
            keys.Add(el.GetProperty("k").GetString()!);
        }

        await Assert.That(keys).IsEquivalentTo(new[] { "A" });
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetPagedAsync_BasePath_WithQuery_AppendsWithAmpersand()
    {
        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""[{"k":"A"}]""", Encoding.UTF8, "application/json");
            return r;
        });
        using var http = MakeClient(inner);
        var client = new TrackerClient(http);

        _ = await client.GetPagedAsync("items?type=bug").GetAsyncEnumerator().MoveNextAsync();

        var url = inner.Seen[0].RequestUri!.ToString();
        await Assert.That(url).Contains("items?type=bug&perPage=50&page=1");
    }

    [Test]
    public async Task GetAsync_EmptyBody_Returns_DefaultJsonElement()
    {
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new ByteArrayContent(Array.Empty<byte>());
            r.Content.Headers.ContentLength = 0;
            return r;
        });
        using var http = MakeClient(inner);
        var client = new TrackerClient(http);

        var result = await client.GetAsync("empty");
        await Assert.That(result.ValueKind).IsEqualTo(JsonValueKind.Undefined);
    }

    [Test]
    public async Task PostJsonAsync_ErrorBody_IsIncludedInExceptionMessage()
    {
        var inner = new TestHttpMessageHandler();
        inner.Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.BadRequest);
            r.Content = new StringContent(
                """{"errors":{"summary":"required"}}""", Encoding.UTF8, "application/json");
            return r;
        });
        using var http = MakeClient(inner);
        var client = new TrackerClient(http);

        using var doc = JsonDocument.Parse("""{"foo":"bar"}""");
        var ex = await Assert.ThrowsAsync<TrackerException>(() => client.PostJsonAsync("issues", doc.RootElement));
        await Assert.That(ex!.Message).Contains("summary");
        await Assert.That(ex.Message).Contains("required");
    }
}
