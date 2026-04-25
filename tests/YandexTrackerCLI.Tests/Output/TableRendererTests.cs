namespace YandexTrackerCLI.Tests.Output;

using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Output;

/// <summary>
/// Тесты <see cref="TableRenderer"/>: 2-column key-value для одиночной сущности,
/// многоколоночная — для массивов объектов.
/// </summary>
public sealed class TableRendererTests
{
    private const int FixedWidth = 100;

    private static string Render(string json, int width = FixedWidth)
    {
        using var doc = JsonDocument.Parse(json);
        var sw = new StringWriter();
        TableRenderer.Render(sw, doc.RootElement, terminalWidth: width);
        return sw.ToString();
    }

    [Test]
    public async Task SingleObject_RendersAs_KeyValueTable_WithHeaders()
    {
        var output = Render("""{"key":"TECH-1","summary":"hello"}""");
        await Assert.That(output).Contains("key");
        await Assert.That(output).Contains("value");
        await Assert.That(output).Contains("TECH-1");
        await Assert.That(output).Contains("hello");
        // Юникод U+2500 для разделителя.
        await Assert.That(output).Contains("─");
    }

    [Test]
    public async Task SingleObject_NestedObject_FlattensViaIdentifyingField()
    {
        // status.display подставляется как значение "status".
        var output = Render("""{"key":"X-1","status":{"key":"open","display":"Открыт"}}""");
        await Assert.That(output).Contains("Открыт");
    }

    [Test]
    public async Task SingleObject_NestedArrayOfPrimitives_RendersInline()
    {
        var output = Render("""{"key":"X","tags":["a","b","c"]}""");
        await Assert.That(output).Contains("[a, b, c]");
    }

    [Test]
    public async Task SingleObject_NestedArrayOfObjects_RendersAsCount()
    {
        var output = Render("""{"key":"X","links":[{"a":1},{"a":2}]}""");
        await Assert.That(output).Contains("[2 items]");
    }

    [Test]
    public async Task ArrayOfObjects_RendersTabular_WithKeyAsFirstColumn()
    {
        var output = Render(
            """[{"key":"TECH-1","summary":"Foo"},{"key":"TECH-2","summary":"Bar"}]""");
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // Первая строка — заголовок, начинается с "key".
        await Assert.That(lines[0].TrimStart()).StartsWith("key");
        // Дальше separator (─), затем 2 строки данных.
        await Assert.That(output).Contains("TECH-1");
        await Assert.That(output).Contains("TECH-2");
        await Assert.That(output).Contains("Foo");
        await Assert.That(output).Contains("Bar");
    }

    [Test]
    public async Task LongStringValue_TruncatedTo_EllipsisChar()
    {
        // Очень длинный summary должен быть обрезан с '…' в конце на узкой ширине.
        var json = """{"key":"K","summary":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""";
        var output = Render(json, width: 30);
        // В key-value таблице keyCol="key" (3 chars), valueCol = 30-3-2 = 25.
        // Длинное значение truncated.
        await Assert.That(output).Contains("…");
    }

    [Test]
    public async Task ArrayOfObjects_TruncatesAt_50Rows_AndEmitsCounter()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('[');
        for (var i = 0; i < 75; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"key\":\"K-").Append(i).Append("\"}");
        }
        sb.Append(']');
        var output = Render(sb.ToString());
        await Assert.That(output).Contains("(50 of 75 rows shown)");
    }

    [Test]
    public async Task EmptyObject_RendersPlaceholder()
    {
        var output = Render("{}");
        await Assert.That(output).Contains("(empty object)");
    }

    [Test]
    public async Task EmptyArray_RendersPlaceholder()
    {
        var output = Render("[]");
        await Assert.That(output).Contains("(empty array)");
    }

    [Test]
    public async Task ArrayOfPrimitives_RendersBulletList()
    {
        var output = Render("""["alpha","beta"]""");
        await Assert.That(output).Contains("- alpha");
        await Assert.That(output).Contains("- beta");
    }

    [Test]
    public async Task ArrayOfObjects_PrefersKeyOverId_AsFirstColumn()
    {
        var output = Render("""[{"id":"1","key":"TECH-1"},{"id":"2","key":"TECH-2"}]""");
        var firstLine = output.Split('\n')[0].TrimEnd('\r', ' ');
        // Заголовок начинается с "key", не с "id".
        await Assert.That(firstLine.TrimStart()).StartsWith("key");
    }

    [Test]
    public async Task SingleObject_LineBreaksInValue_ReplacedWithSpaces()
    {
        var output = Render("""{"key":"X","summary":"line1\nline2"}""");
        // Результат не должен содержать лишних переводов строк внутри значения "summary".
        // Считаем, что между разделителем и счётчиком окончания строк примерно соответствует количеству строк.
        await Assert.That(output).Contains("line1 line2");
    }
}
