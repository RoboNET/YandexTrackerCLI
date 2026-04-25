using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Attachment;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt attachment upload &lt;issue-key&gt; &lt;file-path&gt;
/// [--name &lt;override&gt;]</c>: проверяют multipart-POST с именем файла из аргумента
/// и из опции <c>--name</c>, а также валидацию отсутствия файла (exit 2).
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class AttachmentUploadCommandTests
{
    /// <summary>
    /// Успешная загрузка: POST на <c>/issues/{key}/attachments</c>, Content-Type —
    /// <c>multipart/form-data</c>, в теле присутствует исходное имя файла и байты.
    /// </summary>
    [Test]
    public async Task Upload_PostsMultipart_WithFileName_FromArg()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var tempFile = Path.Combine(Path.GetTempPath(), "yt-upload-" + Guid.NewGuid().ToString("N") + ".bin");
        var payload = Encoding.UTF8.GetBytes("HELLO-TRACKER");
        await File.WriteAllBytesAsync(tempFile, payload);

        try
        {
            HttpMethod? method = null;
            string? path = null;
            string? contentType = null;
            byte[]? body = null;
            var inner = new TestHttpMessageHandler().Push(req =>
            {
                method = req.Method;
                path = req.RequestUri!.AbsolutePath;
                contentType = req.Content!.Headers.ContentType?.MediaType;
                body = req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("""{"id":"att-1"}""", Encoding.UTF8, "application/json"),
                };
            });
            env.InnerHandler = inner;

            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(new[] { "attachment", "upload", "DEV-1", tempFile }, sw, er);

            await Assert.That(exit).IsEqualTo(0);
            await Assert.That(method).IsEqualTo(HttpMethod.Post);
            await Assert.That(path!.EndsWith("/issues/DEV-1/attachments", StringComparison.Ordinal)).IsTrue();
            await Assert.That(contentType).IsEqualTo("multipart/form-data");

            var bodyText = Encoding.UTF8.GetString(body!);
            var expectedName = Path.GetFileName(tempFile);
            await Assert.That(bodyText.Contains($"filename={expectedName}") || bodyText.Contains($"filename=\"{expectedName}\"")).IsTrue();
            await Assert.That(bodyText.Contains("HELLO-TRACKER")).IsTrue();
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Опция <c>--name renamed.bin</c> переопределяет имя файла в теле multipart.
    /// </summary>
    [Test]
    public async Task Upload_WithOverrideName_UsesOverride()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var tempFile = Path.Combine(Path.GetTempPath(), "yt-upload-orig-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllBytesAsync(tempFile, Encoding.UTF8.GetBytes("ABC"));

        try
        {
            byte[]? body = null;
            var inner = new TestHttpMessageHandler().Push(req =>
            {
                body = req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("""{"id":"att-2"}""", Encoding.UTF8, "application/json"),
                };
            });
            env.InnerHandler = inner;

            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(
                new[] { "attachment", "upload", "DEV-1", tempFile, "--name", "renamed.bin" },
                sw,
                er);

            await Assert.That(exit).IsEqualTo(0);
            var bodyText = Encoding.UTF8.GetString(body!);
            await Assert.That(bodyText.Contains("filename=renamed.bin") || bodyText.Contains("filename=\"renamed.bin\"")).IsTrue();
            // И оригинальное имя файла на диске в теле НЕ присутствует как filename=...
            var origName = Path.GetFileName(tempFile);
            await Assert.That(bodyText.Contains($"filename={origName}") || bodyText.Contains($"filename=\"{origName}\"")).IsFalse();
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Несуществующий путь — exit 2, stderr содержит <c>error.code == "invalid_args"</c>,
    /// HTTP-запрос не отправляется.
    /// </summary>
    [Test]
    public async Task Upload_FileMissing_Returns_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var bogus = Path.Combine(Path.GetTempPath(), "definitely-not-here-" + Guid.NewGuid().ToString("N"));

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "attachment", "upload", "DEV-1", bogus }, sw, er);

        await Assert.That(exit).IsEqualTo(2);
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
    }
}
