using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Attachment;

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты read-only guard'а на уровне CLI-команд: проверяют, что mutating
/// attachment-команды (<c>upload</c>/<c>delete</c>) блокируются
/// <see cref="YandexTrackerCLI.Core.Http.ReadOnlyGuardHandler"/>, когда профиль помечен
/// <c>read_only:true</c>. Ожидаемое поведение: exit-code <c>3</c>, stderr — структурированный
/// JSON с <c>error.code = "read_only_mode"</c>. Дополнительно: GET-операции
/// (<c>list</c>, <c>download</c>) не блокируются. Мутируют глобальное state
/// (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class AttachmentReadOnlyGuardTests
{
    /// <summary>
    /// Профиль с <c>read_only:true</c> — guard должен срабатывать на все mutating операции.
    /// </summary>
    private const string ReadOnlyConfig =
        """{"default_profile":"ro","profiles":{"ro":{"org_type":"cloud","org_id":"o","read_only":true,"auth":{"type":"oauth","token":"y0_X"}}}}""";

    /// <summary>
    /// Проверяет, что команда вернула exit 3 и stderr содержит JSON с
    /// <c>error.code = "read_only_mode"</c>.
    /// </summary>
    /// <param name="exit">Exit-code команды.</param>
    /// <param name="stderr">Содержимое перехваченного stderr.</param>
    /// <returns>Task, завершающийся после выполнения всех ассершнов.</returns>
    private static async Task AssertReadOnlyExit(int exit, StringWriter stderr)
    {
        await Assert.That(exit).IsEqualTo(3);
        using var doc = JsonDocument.Parse(stderr.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("read_only_mode");
    }

    /// <summary>
    /// <c>attachment upload</c> в read-only-профиле — блокируется, HTTP не отправляется.
    /// </summary>
    [Test]
    public async Task AttachmentUpload_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var tempFile = Path.Combine(Path.GetTempPath(), "yt-ro-upload-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllBytesAsync(tempFile, new byte[] { 1, 2, 3 });
        try
        {
            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(
                new[] { "attachment", "upload", "DEV-1", tempFile },
                sw,
                er);
            await AssertReadOnlyExit(exit, er);
            await Assert.That(inner.Seen.Count).IsEqualTo(0);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// <c>attachment delete</c> в read-only-профиле — блокируется.
    /// </summary>
    [Test]
    public async Task AttachmentDelete_ReadOnlyProfile_Blocked_Exit3()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        env.InnerHandler = new TestHttpMessageHandler();
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "attachment", "delete", "DEV-1", "att-1" },
            sw,
            er);
        await AssertReadOnlyExit(exit, er);
    }

    /// <summary>
    /// GET-операция <c>attachment list</c> не mutating и должна проходить даже в
    /// read-only-профиле. Exit = 0.
    /// </summary>
    [Test]
    public async Task AttachmentList_ReadOnlyProfile_NotBlocked()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
            return r;
        });
        env.InnerHandler = inner;
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "attachment", "list", "DEV-1" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
    }

    /// <summary>
    /// GET-операция <c>attachment download</c> не mutating и должна проходить даже в
    /// read-only-профиле. Exit = 0; файл записывается.
    /// </summary>
    [Test]
    public async Task AttachmentDownload_ReadOnlyProfile_NotBlocked()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyConfig);

        var bytes = new byte[] { 9, 8, 7, 6 };
        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new ByteArrayContent(bytes);
            r.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            r.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "ro-blob.bin",
            };
            return r;
        });
        env.InnerHandler = inner;

        var target = Path.Combine(
            Path.GetTempPath(),
            "yt-ro-dl-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            var sw = new StringWriter();
            var er = new StringWriter();
            var exit = await env.Invoke(
                new[] { "attachment", "download", "DEV-1", "att-1", "--out", target },
                sw,
                er);
            await Assert.That(exit).IsEqualTo(0);
            await Assert.That(File.Exists(target)).IsTrue();
        }
        finally
        {
            try { File.Delete(target); } catch { /* best effort */ }
        }
    }
}
