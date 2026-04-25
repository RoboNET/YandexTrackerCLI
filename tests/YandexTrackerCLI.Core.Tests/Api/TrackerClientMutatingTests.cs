namespace YandexTrackerCLI.Core.Tests.Api;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Core.Api;
using YandexTrackerCLI.Core.Api.Errors;
using Http;

public sealed class TrackerClientMutatingTests
{
    private static HttpClient MakeClient(TestHttpMessageHandler inner) =>
        new(inner) { BaseAddress = new Uri("https://api.tracker.yandex.net/v3/") };

    [Test]
    public async Task PostJsonRawAsync_SendsRawBody_WithJsonContentType()
    {
        string? capturedBody = null;
        string? capturedContentType = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedContentType = req.Content.Headers.ContentType!.MediaType;
            var r = new HttpResponseMessage(HttpStatusCode.Created);
            r.Content = new StringContent("""{"key":"DEV-1"}""", Encoding.UTF8, "application/json");
            return r;
        });
        using var http = MakeClient(inner);
        var client = new TrackerClient(http);

        var body = """{"summary":"hello","queue":"DEV"}""";
        var result = await client.PostJsonRawAsync("issues", body);

        await Assert.That(result.GetProperty("key").GetString()).IsEqualTo("DEV-1");
        await Assert.That(capturedBody).IsEqualTo(body);
        await Assert.That(capturedContentType).IsEqualTo("application/json");
        await Assert.That(inner.Seen[0].Method).IsEqualTo(HttpMethod.Post);
    }

    [Test]
    public async Task PatchJsonAsync_UsesPatchMethod_ReturnsResponse()
    {
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"updated":true}""", Encoding.UTF8, "application/json");
            return r;
        });
        using var http = MakeClient(inner);
        var client = new TrackerClient(http);

        var result = await client.PatchJsonAsync("issues/DEV-1", """{"summary":"upd"}""");

        await Assert.That(result.GetProperty("updated").GetBoolean()).IsTrue();
        await Assert.That(inner.Seen[0].Method).IsEqualTo(HttpMethod.Patch);
    }

    [Test]
    public async Task DeleteAsync_204_ReturnsUndefinedJsonElement()
    {
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.NoContent);
            r.Content = new StringContent(string.Empty);
            r.Content.Headers.ContentLength = 0;
            return r;
        });
        using var http = MakeClient(inner);
        var client = new TrackerClient(http);

        var result = await client.DeleteAsync("issues/DEV-1");

        await Assert.That(result.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await Assert.That(inner.Seen[0].Method).IsEqualTo(HttpMethod.Delete);
    }

    [Test]
    public async Task DeleteAsync_404_ThrowsNotFound()
    {
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = MakeClient(inner);
        var client = new TrackerClient(http);

        var ex = await Assert.ThrowsAsync<TrackerException>(() => client.DeleteAsync("issues/DEV-1"));
        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.NotFound);
    }

    [Test]
    public async Task PatchJsonAsync_400_WithBody_IncludesInExceptionMessage()
    {
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.BadRequest);
            r.Content = new StringContent("""{"errors":{"summary":"too long"}}""", Encoding.UTF8, "application/json");
            return r;
        });
        using var http = MakeClient(inner);
        var client = new TrackerClient(http);

        var ex = await Assert.ThrowsAsync<TrackerException>(() =>
            client.PatchJsonAsync("issues/DEV-1", """{"summary":"x"}"""));
        await Assert.That(ex!.Message).Contains("too long");
    }
}
