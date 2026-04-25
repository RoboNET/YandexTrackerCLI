namespace YandexTrackerCLI.Core.Tests.Api;

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using TUnit.Core;
using YandexTrackerCLI.Core.Api;
using YandexTrackerCLI.Core.Api.Errors;
using Http;

public sealed class TrackerClientStreamingTests
{
    [Test]
    public async Task GetStreamingAsync_ReturnsStream_WithContentLengthAndFileName()
    {
        var bytes = Encoding.UTF8.GetBytes("download-payload-" + new string('x', 1024));
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new ByteArrayContent(bytes);
            r.Content.Headers.ContentLength = bytes.Length;
            r.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "report.pdf" };
            r.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return r;
        });
        using var http = new HttpClient(inner);
        http.BaseAddress = new Uri("https://api.tracker.yandex.net/v3/");
        var client = new TrackerClient(http);

        await using var download = await client.GetStreamingAsync("issues/DEV-1/attachments/42/download");

        await Assert.That(download.ContentLength).IsEqualTo(bytes.Length);
        await Assert.That(download.FileName).IsEqualTo("report.pdf");
        await Assert.That(download.ContentType).IsEqualTo("application/pdf");

        using var ms = new MemoryStream();
        await download.Stream.CopyToAsync(ms);
        await Assert.That(ms.ToArray().Length).IsEqualTo(bytes.Length);
        await Assert.That(ms.ToArray()).IsEquivalentTo(bytes);
    }

    [Test]
    public async Task GetStreamingAsync_404_Throws_NotFound()
    {
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = new HttpClient(inner) { BaseAddress = new Uri("https://api.tracker.yandex.net/v3/") };
        var client = new TrackerClient(http);

        var ex = await Assert.ThrowsAsync<TrackerException>(() => client.GetStreamingAsync("issues/X/attachments/1/download"));
        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.NotFound);
    }
}
