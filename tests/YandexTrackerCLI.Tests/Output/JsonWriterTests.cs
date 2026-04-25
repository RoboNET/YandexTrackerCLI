namespace YandexTrackerCLI.Tests.Output;

using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Output;

public sealed class JsonWriterTests
{
    [Test]
    public async Task Compact_NoIndentation_NoTrailingNewline()
    {
        using var doc = JsonDocument.Parse("""{"a":1,"b":[2,3]}""");
        var sw = new StringWriter();
        JsonWriter.Write(sw, doc.RootElement, OutputFormat.Json, pretty: false);
        await Assert.That(sw.ToString()).IsEqualTo("""{"a":1,"b":[2,3]}""");
    }

    [Test]
    public async Task Pretty_IndentsAndAppendsNewline()
    {
        using var doc = JsonDocument.Parse("""{"a":1}""");
        var sw = new StringWriter();
        JsonWriter.Write(sw, doc.RootElement, OutputFormat.Json, pretty: true);
        var s = sw.ToString();
        await Assert.That(s).Contains("  \"a\": 1");
        await Assert.That(s.EndsWith("\n") || s.EndsWith("\r\n")).IsTrue();
    }

    [Test]
    public async Task Format_Auto_Throws_AsItMustBeResolved()
    {
        // Auto — sentinel, должен быть резолвлен через FormatResolver до вызова JsonWriter.
        using var doc = JsonDocument.Parse("""{"a":1}""");
        var sw = new StringWriter();
        await Assert.That(() =>
            JsonWriter.Write(sw, doc.RootElement, OutputFormat.Auto, pretty: false))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Format_Minimal_DispatchesToMinimalRenderer()
    {
        // Объект с identifying-полем `key` → выведен как `key + newline`.
        using var doc = JsonDocument.Parse("""{"key":"TECH-1","summary":"x"}""");
        var sw = new StringWriter();
        JsonWriter.Write(sw, doc.RootElement, OutputFormat.Minimal, pretty: false);
        await Assert.That(sw.ToString().TrimEnd('\r', '\n')).IsEqualTo("TECH-1");
    }

    [Test]
    public async Task Format_Table_DispatchesToTableRenderer()
    {
        // Один объект → key-value таблица; должны присутствовать заголовки и значения.
        using var doc = JsonDocument.Parse("""{"key":"TECH-1","summary":"hello"}""");
        var sw = new StringWriter();
        JsonWriter.Write(sw, doc.RootElement, OutputFormat.Table, pretty: false);
        var output = sw.ToString();
        await Assert.That(output).Contains("TECH-1");
        await Assert.That(output).Contains("hello");
    }
}
