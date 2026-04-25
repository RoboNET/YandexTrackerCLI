namespace YandexTrackerCLI.Tests.Commands.Skill;

using TUnit.Core;

/// <summary>
/// End-to-end тесты команды <c>yt skill show</c> для новых target'ов
/// (gemini / cursor / copilot). Show должен печатать ровно тот контент,
/// который был бы записан на диск для соответствующего target'а.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class SkillShowExtraTargetsTests
{
    [Test]
    public async Task SkillShow_Gemini_PrintsSameAsClaude()
    {
        using var env = new TestEnv();
        var sw1 = new StringWriter();
        var sw2 = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "show", "--target", "claude" }, sw1, er);
        await env.Invoke(new[] { "skill", "show", "--target", "gemini" }, sw2, er);

        await Assert.That(sw2.ToString()).IsEqualTo(sw1.ToString());
    }

    [Test]
    public async Task SkillShow_Cursor_PrintsMdcFrontmatter()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "skill", "show", "--target", "cursor" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        var output = sw.ToString();
        await Assert.That(output).StartsWith("---\n");
        await Assert.That(output).Contains("description:");
        await Assert.That(output).Contains("globs:");
        await Assert.That(output).Contains("alwaysApply: false");
        await Assert.That(output).Contains("<!-- yt-version: ");
        await Assert.That(output).DoesNotContain("name: yt");
    }

    [Test]
    public async Task SkillShow_Copilot_PrintsApplyToFrontmatter()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "skill", "show", "--target", "copilot" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        var output = sw.ToString();
        await Assert.That(output).StartsWith("---\n");
        await Assert.That(output).Contains("applyTo: \"**\"");
        await Assert.That(output).Contains("description:");
        await Assert.That(output).Contains("<!-- yt-version: ");
        await Assert.That(output).DoesNotContain("name: yt");
    }
}
