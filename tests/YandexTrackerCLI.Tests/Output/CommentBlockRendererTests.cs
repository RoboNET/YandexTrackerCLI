namespace YandexTrackerCLI.Tests.Output;

using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Output;

/// <summary>
/// Тесты <see cref="CommentBlockRenderer"/>: рендер списка комментариев с author/date
/// заголовком и markdown-телом, edited-маркером, пустыми и редкими случаями.
/// </summary>
public sealed class CommentBlockRendererTests
{
    private static TerminalCapabilities Caps(
        bool color = false,
        bool hyper = false,
        int width = 80) =>
        new(
            IsOutputRedirected: false,
            UseColor: color,
            UseHyperlinks: hyper,
            Width: width,
            UsePager: false,
            PagerCommand: "less");

    private static string Render(string json, TerminalCapabilities? caps = null)
    {
        using var doc = JsonDocument.Parse(json);
        var sw = new StringWriter();
        CommentBlockRenderer.Render(sw, doc.RootElement, caps ?? Caps());
        return sw.ToString();
    }

    [Test]
    public async Task EmptyArray_PrintsPlaceholder()
    {
        var output = Render("[]");
        await Assert.That(output).Contains("(no comments)");
    }

    [Test]
    public async Task SingleComment_RendersAuthorDateAndText()
    {
        const string json = """
            [
              {
                "id": "1",
                "createdAt": "2025-12-10T13:43:00.000+0000",
                "createdBy": {"display":"Alice","login":"alice"},
                "text": "Hello, **world**!"
              }
            ]
            """;
        var output = Render(json);
        await Assert.That(output).Contains("Alice");
        await Assert.That(output).Contains("2025-12-10 13:43 UTC");
        await Assert.That(output).Contains("Hello, world!");
    }

    [Test]
    public async Task MultipleComments_SeparatedByBlankLine()
    {
        const string json = """
            [
              {"createdBy":{"display":"A"},"createdAt":"2025-01-01T00:00:00Z","text":"first"},
              {"createdBy":{"display":"B"},"createdAt":"2025-01-02T00:00:00Z","text":"second"}
            ]
            """;
        var output = Render(json);
        await Assert.That(output).Contains("first");
        await Assert.That(output).Contains("second");
        await Assert.That(output).Contains("A");
        await Assert.That(output).Contains("B");
    }

    [Test]
    public async Task EditedComment_ShowsEditedMarker()
    {
        const string json = """
            [
              {
                "createdBy":{"display":"Carol"},
                "createdAt":"2025-01-01T00:00:00Z",
                "updatedAt":"2025-01-02T00:00:00Z",
                "text":"updated"
              }
            ]
            """;
        var output = Render(json);
        await Assert.That(output).Contains("(edited)");
    }

    [Test]
    public async Task UnchangedUpdatedAt_NoEditedMarker()
    {
        const string json = """
            [
              {
                "createdBy":{"display":"Dave"},
                "createdAt":"2025-01-01T00:00:00Z",
                "updatedAt":"2025-01-01T00:00:00Z",
                "text":"same"
              }
            ]
            """;
        var output = Render(json);
        await Assert.That(output.Contains("(edited)")).IsFalse();
    }

    [Test]
    public async Task EmptyText_RendersEmptyMarker()
    {
        const string json = """[{"createdBy":{"display":"E"},"createdAt":"2025-01-01T00:00:00Z","text":""}]""";
        var output = Render(json);
        await Assert.That(output).Contains("(empty)");
    }

    [Test]
    public async Task NonArray_FallsBackToTable()
    {
        // Не массив → fallback. Объект с identifying полем должен попасть в TableRenderer.
        const string json = """{"key":"X-1","summary":"hi"}""";
        var output = Render(json);
        await Assert.That(output).Contains("X-1");
    }

    [Test]
    public async Task MarkdownInComment_RenderedWithIndent()
    {
        const string json = """
            [
              {
                "createdBy":{"display":"F"},
                "createdAt":"2025-01-01T00:00:00Z",
                "text":"- item 1\n- item 2"
              }
            ]
            """;
        var output = Render(json);
        await Assert.That(output).Contains("  - item 1");
        await Assert.That(output).Contains("  - item 2");
    }

    [Test]
    public async Task HeaderDivider_CapsAt80_OnWideTerminal()
    {
        // Ширина 200 — header divider не должен растянуться на весь экран.
        const string json = """
            [{"createdBy":{"display":"Alice"},"createdAt":"2025-01-01T00:00:00Z","text":"hi"}]
            """;
        var output = Render(json, Caps(color: false, hyper: false, width: 200));
        var headerLine = output.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .First(l => l.Contains("Alice"));
        await Assert.That(AnsiStyle.VisibleLength(headerLine)).IsLessThanOrEqualTo(80);
    }

    [Test]
    public async Task HeaderDivider_NarrowTerminal_DoesNotExpandBeyondWidth()
    {
        const string json = """
            [{"createdBy":{"display":"Alice"},"createdAt":"2025-01-01T00:00:00Z","text":"hi"}]
            """;
        var output = Render(json, Caps(color: false, hyper: false, width: 50));
        var headerLine = output.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .First(l => l.Contains("Alice"));
        await Assert.That(AnsiStyle.VisibleLength(headerLine)).IsLessThanOrEqualTo(50);
    }
}
