using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Attachment;

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды
/// <c>yt attachment download &lt;issue-key&gt; &lt;attachment-id&gt; [--out &lt;path&gt;] [--force]</c>:
/// стриминг тела в файл, выбор имени из <c>Content-Disposition</c>/явного <c>--out</c>,
/// защита от перезаписи существующего файла без <c>--force</c>.
/// Мутируют глобальное state (env + Console + AsyncLocal + CWD), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class AttachmentDownloadCommandTests
{
    private static byte[] Payload(int size)
    {
        var buf = new byte[size];
        for (var i = 0; i < size; i++)
        {
            buf[i] = (byte)(i % 251);
        }
        return buf;
    }

    /// <summary>
    /// Без <c>--out</c>: имя файла берётся из <c>Content-Disposition: attachment;
    /// filename=note.txt</c>, файл пишется в текущую рабочую директорию.
    /// </summary>
    [Test]
    public async Task Download_WritesBytesToFile_FromContentDispositionName()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var bytes = Payload(1024);
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new ByteArrayContent(bytes);
            r.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            r.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "note.txt",
            };
            return r;
        });
        env.InnerHandler = inner;

        var tempDir = Path.Combine(Path.GetTempPath(), "yt-dl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var prevCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tempDir);

            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(new[] { "attachment", "download", "DEV-1", "42" }, sw, er);

            await Assert.That(exit).IsEqualTo(0);
            // На macOS текущая директория может быть resolved через /private-симлинк — сравниваем
            // через Path.GetFullPath, чтобы нормализовать обе стороны.
            var target = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "note.txt"));
            await Assert.That(File.Exists(target)).IsTrue();
            var written = await File.ReadAllBytesAsync(target);
            await Assert.That(written.Length).IsEqualTo(bytes.Length);
            await Assert.That(written).IsEquivalentTo(bytes);

            using var doc = JsonDocument.Parse(sw.ToString());
            var reportedPath = doc.RootElement.GetProperty("downloaded").GetString()!;
            await Assert.That(Path.GetFullPath(reportedPath)).IsEqualTo(target);
            await Assert.That(Path.GetFileName(reportedPath)).IsEqualTo("note.txt");
            await Assert.That(doc.RootElement.GetProperty("bytes").GetInt64()).IsEqualTo(1024L);
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// С <c>--out /path/explicit.bin</c>: имя из Content-Disposition игнорируется,
    /// файл пишется по явному пути.
    /// </summary>
    [Test]
    public async Task Download_WithOut_UsesExplicitPath()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var bytes = Payload(64);
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new ByteArrayContent(bytes);
            r.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            r.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "ignored.txt",
            };
            return r;
        });
        env.InnerHandler = inner;

        var explicitPath = Path.Combine(
            Path.GetTempPath(),
            "yt-dl-explicit-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(
                new[] { "attachment", "download", "DEV-1", "42", "--out", explicitPath },
                sw,
                er);

            await Assert.That(exit).IsEqualTo(0);
            await Assert.That(File.Exists(explicitPath)).IsTrue();
            await Assert.That(File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "ignored.txt"))).IsFalse();
            var written = await File.ReadAllBytesAsync(explicitPath);
            await Assert.That(written).IsEquivalentTo(bytes);
        }
        finally
        {
            try { File.Delete(explicitPath); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Content-Disposition с path-traversal именем (<c>../../malicious.txt</c>)
    /// должен быть sanitized до обычного имени файла — запись идёт в текущую рабочую
    /// директорию, наверх выходит нельзя.
    /// </summary>
    [Test]
    public async Task Download_ContentDispositionWithPathTraversal_StrippedToFileNameOnly()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        var tempDir = Path.Combine(Path.GetTempPath(), "dl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var bytes = System.Text.Encoding.UTF8.GetBytes("safe");
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new ByteArrayContent(bytes);
            r.Content.Headers.ContentLength = bytes.Length;
            r.Content.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("attachment")
                {
                    FileName = "../../malicious.txt",
                };
            return r;
        });
        env.InnerHandler = inner;

        var prevCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(tempDir);
        try
        {
            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(new[] { "attachment", "download", "DEV-1", "42" }, sw, er);
            await Assert.That(exit).IsEqualTo(0);

            // Файл должен появиться как malicious.txt в tempDir, НЕ подняться наверх.
            var expected = Path.Combine(tempDir, "malicious.txt");
            await Assert.That(File.Exists(expected)).IsTrue();

            // Родительская директория не должна содержать malicious.txt.
            var parent = Directory.GetParent(tempDir)!.FullName;
            await Assert.That(File.Exists(Path.Combine(parent, "malicious.txt"))).IsFalse();
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Целевой файл уже существует и <c>--force</c> не указан — exit 2,
    /// исходный файл не переписан.
    /// </summary>
    [Test]
    public async Task Download_ExistingFileWithoutForce_Returns_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var bytes = Payload(32);
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new ByteArrayContent(bytes);
            return r;
        });
        env.InnerHandler = inner;

        var target = Path.Combine(
            Path.GetTempPath(),
            "yt-dl-exists-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllTextAsync(target, "original");
        try
        {
            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(
                new[] { "attachment", "download", "DEV-1", "42", "--out", target },
                sw,
                er);

            await Assert.That(exit).IsEqualTo(2);
            using var doc = JsonDocument.Parse(er.ToString());
            await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
                .IsEqualTo("invalid_args");

            var content = await File.ReadAllTextAsync(target);
            await Assert.That(content).IsEqualTo("original");
        }
        finally
        {
            try { File.Delete(target); } catch { /* best effort */ }
        }
    }
}
