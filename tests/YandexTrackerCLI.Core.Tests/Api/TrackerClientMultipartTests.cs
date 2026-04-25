namespace YandexTrackerCLI.Core.Tests.Api;

using System.Net;
using System.Net.Http;
using System.Text;
using TUnit.Core;
using YandexTrackerCLI.Core.Api;
using Http;

public sealed class TrackerClientMultipartTests
{
    [Test]
    public async Task PostMultipartAsync_Sends_MultipartFormData_WithStreamedFile()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"yt-upload-{Guid.NewGuid():N}.bin");
        var payload = Encoding.UTF8.GetBytes("hello-attachment");
        await File.WriteAllBytesAsync(tmp, payload);

        string? contentType = null;
        string? bodyText = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            contentType = req.Content!.Headers.ContentType?.ToString();
            bodyText = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.Created);
            r.Content = new StringContent("""{"id":"123","name":"note.txt"}""", Encoding.UTF8, "application/json");
            return r;
        });
        using var http = new HttpClient(inner);
        http.BaseAddress = new Uri("https://api.tracker.yandex.net/v3/");
        var client = new TrackerClient(http);

        try
        {
            await using var file = File.OpenRead(tmp);
            using var multipart = new MultipartFormDataContent();
            var streamContent = new StreamContent(file);
            multipart.Add(streamContent, name: "file", fileName: "note.txt");

            var result = await client.PostMultipartAsync("issues/DEV-1/attachments", multipart);

            await Assert.That(contentType!).StartsWith("multipart/form-data");
            await Assert.That(bodyText!).Contains("note.txt");
            await Assert.That(bodyText!).Contains("hello-attachment");
            await Assert.That(result.GetProperty("id").GetString()).IsEqualTo("123");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Test]
    public async Task PostMultipartAsync_404_ThrowsNotFound()
    {
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = new HttpClient(inner);
        http.BaseAddress = new Uri("https://api.tracker.yandex.net/v3/");
        var client = new TrackerClient(http);

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("x"), name: "field");

        var ex = await Assert.ThrowsAsync<YandexTrackerCLI.Core.Api.Errors.TrackerException>(
            () => client.PostMultipartAsync("issues/X/attachments", multipart));
        await Assert.That(ex!.Code).IsEqualTo(YandexTrackerCLI.Core.Api.Errors.ErrorCode.NotFound);
    }
}
