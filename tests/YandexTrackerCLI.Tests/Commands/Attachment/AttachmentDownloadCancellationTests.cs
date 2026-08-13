namespace YandexTrackerCLI.Tests.Commands.Attachment;

using System.Net;
using System.Net.Http;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// Тесты поведения <c>yt attachment download</c> при обрыве посреди скачивания.
/// Ключевой инвариант: файл под финальным именем означает полностью скачанное вложение —
/// недописанный результат на диске не остаётся и не может быть принят за целый.
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class AttachmentDownloadCancellationTests
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
    /// Достаёт из stderr последнюю строку-JSON: команда пишет туда ещё и строку
    /// <c>removed incomplete file: …</c>.
    /// </summary>
    /// <param name="stderr">Полное содержимое stderr.</param>
    /// <returns>Разобранный JSON-документ ошибки.</returns>
    private static JsonDocument ParseErrorJson(string stderr)
    {
        var jsonLine = stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Last(l => l.StartsWith('{'));
        return JsonDocument.Parse(jsonLine);
    }

    /// <summary>
    /// Отмена посреди записи в файл: exit 11, JSON-ошибка <c>cancelled</c>,
    /// недописанный файл удалён, факт удаления назван в stderr.
    /// </summary>
    [Test]
    public async Task Download_CancelledMidWrite_RemovesPartialFile_AndReturnsExit11()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        using var cts = new CancellationTokenSource();
        var prefix = Payload(4096);
        env.InnerHandler = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StreamContent(new CancellingSourceStream(prefix, cts));
            return r;
        });

        var target = Path.Combine(
            Path.GetTempPath(),
            "yt-dl-cancel-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(
                new[] { "attachment", "download", "DEV-1", "42", "--out", target },
                sw,
                er,
                cts.Token);

            await Assert.That(exit).IsEqualTo(11);
            await Assert.That(File.Exists(target)).IsFalse();
            await Assert.That(LeftoverParts(target)).IsEmpty();
            await Assert.That(sw.ToString()).IsEqualTo(string.Empty);
            await Assert.That(er.ToString()).Contains("removed incomplete download");

            using var doc = ParseErrorJson(er.ToString());
            await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
                .IsEqualTo("cancelled");
            await Assert.That(er.ToString()).DoesNotContain("   at ");
        }
        finally
        {
            try { File.Delete(target); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Отмена при <c>--force</c> поверх существующего файла: прежнее содержимое остаётся
    /// нетронутым. Скачивание идёт во временный файл и переезжает под целевое имя только
    /// целиком, поэтому оборванная попытка не уничтожает то, что уже было у пользователя.
    /// </summary>
    [Test]
    public async Task Download_CancelledMidWriteWithForce_KeepsPreviousContentIntact()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        using var cts = new CancellationTokenSource();
        var prefix = Payload(4096);
        env.InnerHandler = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StreamContent(new CancellingSourceStream(prefix, cts));
            return r;
        });

        var target = Path.Combine(
            Path.GetTempPath(),
            "yt-dl-cancel-force-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllTextAsync(target, "original");
        try
        {
            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(
                new[] { "attachment", "download", "DEV-1", "42", "--out", target, "--force" },
                sw,
                er,
                cts.Token);

            await Assert.That(exit).IsEqualTo(11);
            await Assert.That(File.Exists(target)).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(target)).IsEqualTo("original");
            await Assert.That(LeftoverParts(target)).IsEmpty();
        }
        finally
        {
            try { File.Delete(target); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Существующий файл, который нельзя открыть на запись (права <c>0444</c>), при
    /// <c>--force</c>: файл пользователя не имеет права исчезнуть. Раньше сбой открытия
    /// приводил к удалению — удаление зависит от прав на каталог, а не на файл, поэтому
    /// команда сносила чужой целый файл, ничего не скачав.
    /// </summary>
    [Test]
    public async Task Download_ForceOverReadOnlyFile_NeverLosesTheFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var bytes = Payload(256);
        env.InnerHandler = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StreamContent(new MemoryStream(bytes));
            r.Content.Headers.ContentLength = bytes.Length;
            return r;
        });

        var target = Path.Combine(
            Path.GetTempPath(),
            "yt-dl-ro-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllTextAsync(target, "original");
        File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        try
        {
            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(
                new[] { "attachment", "download", "DEV-1", "42", "--out", target, "--force" },
                sw,
                er);

            await Assert.That(File.Exists(target)).IsTrue();
            await Assert.That(exit).IsEqualTo(0);
            await Assert.That(await File.ReadAllBytesAsync(target)).IsEquivalentTo(bytes);
            await Assert.That(LeftoverParts(target)).IsEmpty();
        }
        finally
        {
            try
            {
                File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.Delete(target);
            }
            catch
            {
                // best effort
            }
        }
    }

    /// <summary>
    /// Каталог, в котором нельзя создать файл: скачивание падает с понятной ошибкой,
    /// не тронув то, что там уже лежит. Плата за схему «временный файл рядом с целью» —
    /// требуется право записи на каталог, а не только на сам файл.
    /// </summary>
    [Test]
    public async Task Download_IntoUnwritableDirectory_FailsWithoutTouchingExistingFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var dir = Path.Combine(Path.GetTempPath(), "yt-dl-rodir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "keep.bin");
        await File.WriteAllTextAsync(target, "original");
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            // Под root права каталога не ограничивают — проверять нечего.
            if (CanCreateFileIn(dir))
            {
                return;
            }

            using var env = new TestEnv();
            env.SetConfig(TestEnv.MinimalOAuthConfig);
            var bytes = Payload(256);
            env.InnerHandler = new TestHttpMessageHandler().Push(_ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK);
                r.Content = new StreamContent(new MemoryStream(bytes));
                return r;
            });

            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(
                new[] { "attachment", "download", "DEV-1", "42", "--out", target, "--force" },
                sw,
                er);

            await Assert.That(exit).IsEqualTo(8);
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await Assert.That(await File.ReadAllTextAsync(target)).IsEqualTo("original");
        }
        finally
        {
            try
            {
                File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    /// <summary>
    /// Успешное скачивание поверх существующего файла с <c>--force</c>: содержимое заменено
    /// целиком, временный файл не остался.
    /// </summary>
    [Test]
    public async Task Download_ForceOverExistingFile_ReplacesContent_AndLeavesNoTempFile()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var bytes = Payload(1024);
        env.InnerHandler = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StreamContent(new MemoryStream(bytes));
            r.Content.Headers.ContentLength = bytes.Length;
            return r;
        });

        var target = Path.Combine(
            Path.GetTempPath(),
            "yt-dl-replace-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllTextAsync(target, "original");
        try
        {
            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(
                new[] { "attachment", "download", "DEV-1", "42", "--out", target, "--force" },
                sw,
                er);

            await Assert.That(exit).IsEqualTo(0);
            await Assert.That(await File.ReadAllBytesAsync(target)).IsEquivalentTo(bytes);
            await Assert.That(LeftoverParts(target)).IsEmpty();
            await Assert.That(sw.ToString()).Contains("\"bytes\":1024");
        }
        finally
        {
            try { File.Delete(target); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Возвращает временные файлы скачивания, оставшиеся рядом с целью.
    /// </summary>
    /// <param name="target">Целевой путь.</param>
    /// <returns>Пути найденных временных файлов.</returns>
    private static string[] LeftoverParts(string target)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(target))!;
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, Path.GetFileName(target) + ".part-*")
            : Array.Empty<string>();
    }

    /// <summary>
    /// Проверяет, что в каталоге всё-таки можно создать файл (например, процесс работает
    /// от root и права каталога его не ограничивают).
    /// </summary>
    /// <param name="dir">Проверяемый каталог.</param>
    /// <returns><see langword="true"/>, если создать файл удалось.</returns>
    private static bool CanCreateFileIn(string dir)
    {
        var probe = Path.Combine(dir, "probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (File.Create(probe))
            {
            }

            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Тело короче объявленного <c>Content-Length</c> при записи в файл: как и в режиме
    /// <c>--out -</c>, это <c>network_error</c> (exit 8), а усечённый файл удаляется.
    /// </summary>
    [Test]
    public async Task Download_ShorterThanContentLength_RemovesFile_AndReturnsNetworkError()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var bytes = Payload(256);
        env.InnerHandler = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StreamContent(new MemoryStream(bytes));
            r.Content.Headers.ContentLength = 4096;
            return r;
        });

        var target = Path.Combine(
            Path.GetTempPath(),
            "yt-dl-short-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(
                new[] { "attachment", "download", "DEV-1", "42", "--out", target },
                sw,
                er);

            await Assert.That(exit).IsEqualTo(8);
            await Assert.That(File.Exists(target)).IsFalse();

            using var doc = ParseErrorJson(er.ToString());
            await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
                .IsEqualTo("network_error");
            await Assert.That(doc.RootElement.GetProperty("error").GetProperty("message").GetString())
                .Contains("truncated download: expected 4096 bytes, got 256");
        }
        finally
        {
            try { File.Delete(target); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Поток тела ответа, который отдаёт префикс, а затем отменяет переданный
    /// <see cref="CancellationTokenSource"/> и бросает отмену — как если бы посреди
    /// скачивания пришёл Ctrl-C.
    /// </summary>
    /// <param name="prefix">Байты, которые успевают попасть в файл.</param>
    /// <param name="cts">Источник отмены, который поток дёргает после префикса.</param>
    private sealed class CancellingSourceStream(byte[] prefix, CancellationTokenSource cts) : Stream
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

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var remaining = prefix.Length - _position;
            if (remaining <= 0)
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
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
