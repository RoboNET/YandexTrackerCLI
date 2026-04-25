namespace YandexTrackerCLI.Tests.Output;

using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Output;

/// <summary>
/// Тесты <see cref="MinimalRenderer"/>: одно identifying-поле на строку.
/// </summary>
public sealed class MinimalRendererTests
{
    private static string Render(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var sw = new StringWriter();
        MinimalRenderer.Render(sw, doc.RootElement);
        return sw.ToString();
    }

    [Test]
    public async Task Object_With_Key_Field_PrintsKey()
    {
        var output = Render("""{"key":"TECH-1","summary":"Hello"}""");
        await Assert.That(output.TrimEnd('\r', '\n')).IsEqualTo("TECH-1");
    }

    [Test]
    public async Task Object_With_Id_Field_PrintsId_WhenNoKey()
    {
        var output = Render("""{"id":"42","login":"a"}""");
        await Assert.That(output.TrimEnd('\r', '\n')).IsEqualTo("42");
    }

    [Test]
    public async Task Object_With_Login_PrintsLogin()
    {
        var output = Render("""{"login":"jdoe","display":"John"}""");
        await Assert.That(output.TrimEnd('\r', '\n')).IsEqualTo("jdoe");
    }

    [Test]
    public async Task Array_Of_Objects_PrintsEachKey_OnSeparateLine()
    {
        var output = Render("""[{"key":"TECH-1"},{"key":"TECH-2"},{"key":"TECH-3"}]""");
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        await Assert.That(lines.Length).IsEqualTo(3);
        await Assert.That(lines[0].TrimEnd('\r')).IsEqualTo("TECH-1");
        await Assert.That(lines[1].TrimEnd('\r')).IsEqualTo("TECH-2");
        await Assert.That(lines[2].TrimEnd('\r')).IsEqualTo("TECH-3");
    }

    [Test]
    public async Task Object_Without_Identifying_Fields_FallsBackToCompactJson()
    {
        var output = Render("""{"foo":"bar","baz":1}""").TrimEnd('\r', '\n');
        await Assert.That(output).IsEqualTo("""{"foo":"bar","baz":1}""");
    }

    [Test]
    public async Task Primitive_String_PrintsAsIs()
    {
        var output = Render("\"hello world\"");
        await Assert.That(output.TrimEnd('\r', '\n')).IsEqualTo("hello world");
    }

    [Test]
    public async Task Primitive_Number_PrintsRawText()
    {
        var output = Render("42");
        await Assert.That(output.TrimEnd('\r', '\n')).IsEqualTo("42");
    }

    [Test]
    public async Task Array_Of_Primitives_PrintsLineByLine()
    {
        var output = Render("""["a","b","c"]""");
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        await Assert.That(lines.Length).IsEqualTo(3);
        await Assert.That(lines[0].TrimEnd('\r')).IsEqualTo("a");
        await Assert.That(lines[1].TrimEnd('\r')).IsEqualTo("b");
        await Assert.That(lines[2].TrimEnd('\r')).IsEqualTo("c");
    }

    [Test]
    public async Task Object_With_Empty_Identifying_FieldFallsThrough()
    {
        // key="" → пустая строка, переходим к id.
        var output = Render("""{"key":"","id":"42"}""");
        await Assert.That(output.TrimEnd('\r', '\n')).IsEqualTo("42");
    }
}
