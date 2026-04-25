namespace YandexTrackerCLI.Tests.Commands.Skill;

using System.Text.Json;
using TUnit.Core;

/// <summary>
/// End-to-end тесты команды <c>yt skill uninstall</c>.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class SkillUninstallCommandTests
{
    [Test]
    public async Task SkillUninstall_Claude_RemovesFile()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "claude" }, sw, er);
        var claude = Path.Combine(env.Root, "home", ".claude", "skills", "yt", "SKILL.md");
        await Assert.That(File.Exists(claude)).IsTrue();

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "uninstall", "--target", "claude" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(File.Exists(claude)).IsFalse();

        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("uninstalled").GetArrayLength()).IsEqualTo(1);
    }

    [Test]
    public async Task SkillUninstall_Codex_RemovesFile()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "codex" }, sw, er);
        var path = Path.Combine(env.Root, "home", ".agents", "skills", "yt", "SKILL.md");
        await Assert.That(File.Exists(path)).IsTrue();

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "uninstall", "--target", "codex" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(File.Exists(path)).IsFalse();
    }

    [Test]
    public async Task SkillUninstall_NotInstalled_Skipped()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "skill", "uninstall", "--target", "claude" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("uninstalled").GetArrayLength()).IsEqualTo(0);
        await Assert.That(doc.RootElement.GetProperty("skipped").GetArrayLength()).IsEqualTo(1);
    }

    [Test]
    public async Task SkillUninstall_All_RemovesBoth()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install" }, sw, er);
        var claude = Path.Combine(env.Root, "home", ".claude", "skills", "yt", "SKILL.md");
        var codex = Path.Combine(env.Root, "home", ".agents", "skills", "yt", "SKILL.md");
        await Assert.That(File.Exists(claude)).IsTrue();
        await Assert.That(File.Exists(codex)).IsTrue();

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "uninstall" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(File.Exists(claude)).IsFalse();
        await Assert.That(File.Exists(codex)).IsFalse();
    }
}
