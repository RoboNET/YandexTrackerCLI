namespace YandexTrackerCLI.Tests.Output;

using System.Text.Json;
using TUnit.Core;
using Core.Api.Errors;
using YandexTrackerCLI.Output;

public sealed class ErrorWriterTests
{
    [Test]
    public async Task Writes_ErrorJsonEnvelope_ToStderr_WithNewline()
    {
        var ex = new TrackerException(ErrorCode.NotFound, "Issue DEV-1 not found",
            httpStatus: 404, traceId: "abc-123");
        var sw = new StringWriter();
        ErrorWriter.Write(sw, ex);

        var line = sw.ToString().TrimEnd();
        using var doc = JsonDocument.Parse(line);
        var err = doc.RootElement.GetProperty("error");
        await Assert.That(err.GetProperty("code").GetString()).IsEqualTo("not_found");
        await Assert.That(err.GetProperty("message").GetString()).IsEqualTo("Issue DEV-1 not found");
        await Assert.That(err.GetProperty("http_status").GetInt32()).IsEqualTo(404);
        await Assert.That(err.GetProperty("trace_id").GetString()).IsEqualTo("abc-123");
        // newline at the end
        await Assert.That(sw.ToString().EndsWith('\n') || sw.ToString().EndsWith("\r\n")).IsTrue();
    }

    [Test]
    public async Task OmitsOptionalFields_WhenNull()
    {
        var ex = new TrackerException(ErrorCode.ConfigError, "No profile");
        var sw = new StringWriter();
        ErrorWriter.Write(sw, ex);

        using var doc = JsonDocument.Parse(sw.ToString().TrimEnd());
        var err = doc.RootElement.GetProperty("error");
        await Assert.That(err.TryGetProperty("http_status", out _)).IsFalse();
        await Assert.That(err.TryGetProperty("trace_id", out _)).IsFalse();
    }
}
