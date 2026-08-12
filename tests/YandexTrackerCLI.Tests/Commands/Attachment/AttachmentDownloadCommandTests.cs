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

    /// <summary>
    /// <c>--out -</c>: байты вложения уходят в raw-stdout, JSON-сводки в stdout нет,
    /// краткая диагностика пишется в stderr, exit 0.
    /// </summary>
    [Test]
    public async Task Download_OutDash_StreamsBytesToStdout_AndKeepsStdoutClean()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var bytes = Payload(4096);
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new ByteArrayContent(bytes);
            r.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            r.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "note.bin",
            };
            return r;
        });
        env.InnerHandler = inner;

        using var raw = new MemoryStream();
        YandexTrackerCLI.Commands.Attachment.AttachmentDownloadCommand.TestStdoutOverride.Value = raw;

        var tempDir = Path.Combine(Path.GetTempPath(), "yt-dl-stdout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var prevCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tempDir);

            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(
                new[] { "attachment", "download", "DEV-1", "42", "--out", "-" },
                sw,
                er);

            await Assert.That(exit).IsEqualTo(0);
            await Assert.That(raw.ToArray()).IsEquivalentTo(bytes);
            // stdout (TextWriter) должен остаться пустым — никакого JSON-хвоста.
            await Assert.That(sw.ToString()).IsEqualTo(string.Empty);
            await Assert.That(er.ToString()).Contains("downloaded 4096 bytes");
            // На диск ничего не записано.
            await Assert.That(Directory.GetFiles(tempDir)).IsEmpty();
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// <c>--out - --force</c>: бессмысленная комбинация, явная ошибка
    /// <c>invalid_args</c> (exit 2), HTTP-запрос не выполняется.
    /// </summary>
    [Test]
    public async Task Download_OutDashWithForce_Returns_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        using var raw = new MemoryStream();
        YandexTrackerCLI.Commands.Attachment.AttachmentDownloadCommand.TestStdoutOverride.Value = raw;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "attachment", "download", "DEV-1", "42", "--out", "-", "--force" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(2);
        await Assert.That(raw.Length).IsEqualTo(0L);
        await Assert.That(sw.ToString()).IsEqualTo(string.Empty);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
    }

    /// <summary>
    /// Обрыв пайпа при записи в stdout (потребитель закрылся, как при <c>| head -c 100</c>):
    /// команда завершается штатно с кодом 0, в stderr — явная пометка про усечение,
    /// а не обычная сводка полной передачи.
    /// </summary>
    [Test]
    public async Task Download_OutDash_BrokenPipe_Exits0()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var bytes = Payload(256 * 1024);
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new ByteArrayContent(bytes);
            return r;
        });
        env.InnerHandler = inner;

        // 100 KiB: первый чанк (80 KiB) проходит целиком, на втором пайп рвётся.
        const int accept = 100 * 1024;
        using var raw = new BrokenPipeStream(acceptBytes: accept);
        YandexTrackerCLI.Commands.Attachment.AttachmentDownloadCommand.TestStdoutOverride.Value = raw;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "attachment", "download", "DEV-1", "42", "--out", "-" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(sw.ToString()).IsEqualTo(string.Empty);
        // Потребитель принял ровно свой лимит и ни байтом больше.
        await Assert.That(raw.AcceptedBytes).IsEqualTo(accept);
        // Полностью записанным считается только первый чанк — частично принятая запись
        // не засчитывается, и сводка обязана отличаться от полной передачи.
        await Assert.That(er.ToString())
            .Contains("downloaded 81920 bytes (truncated: consumer closed the pipe)");
        await Assert.That(er.ToString()).DoesNotContain("error");
    }

    /// <summary>
    /// Тело HTTP-ответа обрывается посреди скачивания (<see cref="HttpIOException"/>,
    /// наследник <see cref="IOException"/>): это НЕ обрыв пайпа, потребителю нельзя
    /// отдавать усечённые данные под exit 0 — ожидается <c>network_error</c> (exit 8).
    /// </summary>
    [Test]
    public async Task Download_OutDash_ResponseStreamFails_ReturnsNetworkError()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        const int served = 512;
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StreamContent(new FailingSourceStream(Payload(served)));
            return r;
        });
        env.InnerHandler = inner;

        using var raw = new MemoryStream();
        YandexTrackerCLI.Commands.Attachment.AttachmentDownloadCommand.TestStdoutOverride.Value = raw;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "attachment", "download", "DEV-1", "42", "--out", "-" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(8);
        await Assert.That(sw.ToString()).IsEqualTo(string.Empty);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("network_error");
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("message").GetString())
            .Contains("response stream failed after 512 bytes");
    }

    /// <summary>
    /// Тело ответа короче объявленного <c>Content-Length</c> (сервер закрыл соединение
    /// без ошибки чтения): усечение обязано стать ошибкой, а не exit 0.
    /// </summary>
    [Test]
    public async Task Download_OutDash_ShorterThanContentLength_ReturnsNetworkError()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var bytes = Payload(256);
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StreamContent(new MemoryStream(bytes));
            r.Content.Headers.ContentLength = 4096;
            return r;
        });
        env.InnerHandler = inner;

        using var raw = new MemoryStream();
        YandexTrackerCLI.Commands.Attachment.AttachmentDownloadCommand.TestStdoutOverride.Value = raw;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "attachment", "download", "DEV-1", "42", "--out", "-" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(8);
        await Assert.That(sw.ToString()).IsEqualTo(string.Empty);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("network_error");
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("message").GetString())
            .Contains("truncated download: expected 4096 bytes, got 256");
    }

    /// <summary>
    /// Ошибка записи в stdout, не являющаяся обрывом пайпа (например ENOSPC при
    /// <c>&gt; file</c> на заполненном диске): должна стать ошибкой, а не exit 0.
    /// </summary>
    [Test]
    public async Task Download_OutDash_NonPipeWriteFailure_ReturnsNetworkError()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var bytes = Payload(4096);
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new ByteArrayContent(bytes);
            return r;
        });
        env.InnerHandler = inner;

        const int enospc = 28;
        using var raw = new BrokenPipeStream(acceptBytes: 0, hresult: enospc);
        YandexTrackerCLI.Commands.Attachment.AttachmentDownloadCommand.TestStdoutOverride.Value = raw;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "attachment", "download", "DEV-1", "42", "--out", "-" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(8);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("network_error");
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("message").GetString())
            .Contains("failed to write to stdout");
    }

    /// <summary>
    /// HResult, который среда кладёт в <see cref="IOException"/> при записи
    /// в закрытый пайп: сырой <c>EPIPE</c> на Unix, <c>ERROR_BROKEN_PIPE</c> на Windows.
    /// </summary>
    private static int BrokenPipeHResult => OperatingSystem.IsWindows() ? 109 : 32;

    /// <summary>
    /// Поток, имитирующий оборванный пайп: принимает ограниченное число байт,
    /// затем бросает <see cref="IOException"/> с тем же HResult, что и ядро.
    /// Асинхронный путь переопределён напрямую — production пишет
    /// <c>WriteAsync(ReadOnlyMemory&lt;byte&gt;, CancellationToken)</c>.
    /// </summary>
    /// <param name="acceptBytes">Сколько байт потребитель успевает принять.</param>
    /// <param name="hresult">HResult бросаемого <see cref="IOException"/>.</param>
    private sealed class BrokenPipeStream(int acceptBytes, int? hresult = null) : Stream
    {
        private int _written;

        /// <summary>
        /// Gets the number of bytes actually accepted before the simulated failure.
        /// </summary>
        public int AcceptedBytes => _written;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _written;

        public override long Position
        {
            get => _written;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            Accept(count);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Accept(buffer.Length);
            return ValueTask.CompletedTask;
        }

        private void Accept(int count)
        {
            var accepted = Math.Min(count, Math.Max(acceptBytes - _written, 0));
            // Счётчик растёт только на реально принятые байты — иначе Length завышал бы
            // то, что увидел потребитель.
            _written += accepted;
            if (accepted < count)
            {
                throw new IOException("Broken pipe", hresult ?? BrokenPipeHResult);
            }
        }
    }

    /// <summary>
    /// Поток тела ответа, который отдаёт заданный префикс, а затем обрывается
    /// как настоящее соединение — <see cref="HttpIOException"/> (наследник
    /// <see cref="IOException"/>).
    /// </summary>
    /// <param name="prefix">Байты, успевающие дойти до потребителя.</param>
    private sealed class FailingSourceStream(byte[] prefix) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var remaining = prefix.Length - _position;
            if (remaining <= 0)
            {
                throw new HttpIOException(
                    HttpRequestError.ResponseEnded,
                    "The response ended prematurely.");
            }

            var n = Math.Min(remaining, buffer.Length);
            prefix.AsSpan(_position, n).CopyTo(buffer);
            _position += n;
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));
    }
}
