namespace YandexTrackerCLI.Tests.Commands.Issue;

using System.CommandLine;
using System.IO;
using TUnit.Core;
using YandexTrackerCLI.Commands;

/// <summary>
/// Тесты структуры subtree <c>yt issue</c> и <c>yt comment</c>.
/// </summary>
public sealed class IssueSubtreeTests
{
    /// <summary>
    /// <c>yt issue --help</c> должен перечислить все 8 placeholder подкоманд.
    /// </summary>
    [Test]
    public async Task IssueHelp_ListsAllSubcommands()
    {
        var root = RootCommandBuilder.Build();
        var sw = new StringWriter();
        var cfg = new InvocationConfiguration { Output = sw, Error = sw };
        _ = await root.Parse(new[] { "issue", "--help" }).InvokeAsync(cfg);
        var text = sw.ToString();

        foreach (var sub in new[] { "get", "find", "create", "update", "transition", "move", "delete", "batch" })
        {
            await Assert.That(text).Contains(sub);
        }
    }

    /// <summary>
    /// <c>yt comment --help</c> должен перечислить все 4 placeholder подкоманды.
    /// </summary>
    [Test]
    public async Task CommentHelp_ListsAllSubcommands()
    {
        var root = RootCommandBuilder.Build();
        var sw = new StringWriter();
        var cfg = new InvocationConfiguration { Output = sw, Error = sw };
        _ = await root.Parse(new[] { "comment", "--help" }).InvokeAsync(cfg);
        var text = sw.ToString();

        foreach (var sub in new[] { "list", "add", "update", "delete" })
        {
            await Assert.That(text).Contains(sub);
        }
    }
}
