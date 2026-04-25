namespace YandexTrackerCLI.Tests.Output;

using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Output;

/// <summary>
/// Тесты <see cref="IssueDetailRenderer"/>: сценарии полного рендера issue,
/// извлечения display-полей, форматирования timestamp'ов и markdown-описания.
/// </summary>
public sealed class IssueDetailRendererTests
{
    private const string Esc = "";

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
        IssueDetailRenderer.Render(sw, doc.RootElement, caps ?? Caps());
        return sw.ToString();
    }

    [Test]
    public async Task FullIssue_ContainsKeyTypeStatusPriorityAndSummary()
    {
        const string json = """
            {
              "key": "TECH-1",
              "summary": "Fix login bug",
              "type": {"key":"bug","display":"Bug"},
              "status": {"key":"open","display":"Open"},
              "priority": {"key":"normal","display":"Normal"},
              "queue": {"display":"Tech Debt","key":"TECH"},
              "assignee": {"display":"John","login":"jdoe"},
              "createdAt": "2025-12-10T13:43:00.000+0000",
              "updatedAt": "2025-12-12T08:00:00.000+0000",
              "createdBy": {"display":"Alice","login":"alice"},
              "updatedBy": {"display":"Bob","login":"bob"},
              "tags": ["urgent","backend"],
              "description": "## Steps\n\n1. login\n2. observe error"
            }
            """;
        var output = Render(json);
        await Assert.That(output).Contains("TECH-1");
        await Assert.That(output).Contains("Bug");
        await Assert.That(output).Contains("Open");
        await Assert.That(output).Contains("Normal");
        await Assert.That(output).Contains("Fix login bug");
    }

    [Test]
    public async Task QueueLine_ContainsDisplayAndKey()
    {
        const string json = """
            {
              "key":"X-1",
              "summary":"s",
              "queue":{"display":"Engineering","key":"ENG"}
            }
            """;
        var output = Render(json);
        await Assert.That(output).Contains("Engineering (ENG)");
    }

    [Test]
    public async Task MissingFields_RenderAsEmDash()
    {
        const string json = """
            {"key":"K","summary":"only key"}
            """;
        var output = Render(json);
        // Assignee/Tags/Created/Updated/Queue все отсутствуют → каждое поле с em-dash.
        await Assert.That(output).Contains("Assignee");
        await Assert.That(output).Contains("—");
    }

    [Test]
    public async Task Timestamp_FormattedAsYearMonthDayHourMinuteUtc()
    {
        const string json = """
            {"key":"K","summary":"s","createdAt":"2025-12-10T13:43:25.000+0000"}
            """;
        var output = Render(json);
        await Assert.That(output).Contains("2025-12-10 13:43 UTC");
    }

    [Test]
    public async Task Tags_JoinedByComma()
    {
        const string json = """
            {"key":"K","summary":"s","tags":["a","b","c"]}
            """;
        var output = Render(json);
        await Assert.That(output).Contains("a, b, c");
    }

    [Test]
    public async Task Description_RenderedAsMarkdown_WithIndent()
    {
        const string json = """
            {"key":"K","summary":"s","description":"# Heading\n\nBody text"}
            """;
        var output = Render(json);
        await Assert.That(output).Contains("Description");
        await Assert.That(output).Contains("Heading");
        await Assert.That(output).Contains("Body text");
        // Description rendered with leftIndent=2 — две пробельные позиции до текста.
        await Assert.That(output).Contains("  Heading");
    }

    [Test]
    public async Task IssueKey_LinkifiedWhenHyperlinksEnabled()
    {
        const string json = """{"key":"DEV-42","summary":"s"}""";
        var output = Render(json, Caps(color: false, hyper: true));
        await Assert.That(output).Contains($"{Esc}]8;;https://tracker.yandex.ru/DEV-42{Esc}\\");
    }

    [Test]
    public async Task SectionDivider_DescriptionPrintedOnlyWhenDescriptionPresent()
    {
        const string jsonNoDesc = """{"key":"K","summary":"s"}""";
        var output = Render(jsonNoDesc);
        await Assert.That(output.Contains("Description")).IsFalse();
    }

    [Test]
    public async Task SectionDivider_CapsAt80_OnWideTerminal()
    {
        // Ширина терминала 200 — divider не должен растянуться на всю ширину.
        const string json = """{"key":"K","summary":"s","description":"body"}""";
        var output = Render(json, Caps(color: false, hyper: false, width: 200));
        var dividerLine = output.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .First(l => l.Contains("Description"));
        await Assert.That(AnsiStyle.VisibleLength(dividerLine)).IsLessThanOrEqualTo(80);
    }

    [Test]
    public async Task SectionDivider_NarrowTerminal_DoesNotExpandBeyondWidth()
    {
        // На узком терминале divider остаётся в пределах caps.Width (не вылазит за 80).
        const string json = """{"key":"K","summary":"s","description":"body"}""";
        var output = Render(json, Caps(color: false, hyper: false, width: 50));
        var dividerLine = output.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .First(l => l.Contains("Description"));
        await Assert.That(AnsiStyle.VisibleLength(dividerLine)).IsLessThanOrEqualTo(50);
    }
}
