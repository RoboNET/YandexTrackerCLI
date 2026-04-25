namespace YandexTrackerCLI.Tests.Commands.Skill;

using TUnit.Core;

/// <summary>
/// End-to-end тесты команды <c>yt skill show</c>.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class SkillShowCommandTests
{
    [Test]
    public async Task SkillShow_Claude_PrintsFullContent()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "skill", "show", "--target", "claude" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        var output = sw.ToString();
        await Assert.That(output).Contains("---\nname: yt");
        await Assert.That(output).Contains("# yt — Yandex Tracker CLI");
        await Assert.That(output).Contains("<!-- yt-version:");
    }

    [Test]
    public async Task SkillShow_Codex_PrintsSameContentAsClaude()
    {
        using var env = new TestEnv();
        var sw1 = new StringWriter();
        var sw2 = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "show", "--target", "claude" }, sw1, er);
        await env.Invoke(new[] { "skill", "show", "--target", "codex" }, sw2, er);

        await Assert.That(sw2.ToString()).IsEqualTo(sw1.ToString());
    }
}
