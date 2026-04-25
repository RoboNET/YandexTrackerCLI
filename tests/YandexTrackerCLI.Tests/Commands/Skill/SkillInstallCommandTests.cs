namespace YandexTrackerCLI.Tests.Commands.Skill;

using System.Text.Json;
using TUnit.Core;

/// <summary>
/// End-to-end тесты команды <c>yt skill install</c>.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class SkillInstallCommandTests
{
    [Test]
    public async Task SkillInstall_ClaudeGlobal_CreatesFile()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[] { "skill", "install", "--target", "claude", "--scope", "global" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        var expected = Path.Combine(env.Root, "home", ".claude", "skills", "yt", "SKILL.md");
        await Assert.That(File.Exists(expected)).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        var arr = doc.RootElement.GetProperty("installed").EnumerateArray().ToArray();
        await Assert.That(arr.Length).IsEqualTo(1);
        await Assert.That(arr[0].GetProperty("path").GetString()).IsEqualTo(expected);
        await Assert.That(arr[0].GetProperty("target").GetString()).IsEqualTo("claude");
        await Assert.That(arr[0].GetProperty("scope").GetString()).IsEqualTo("global");
    }

    [Test]
    public async Task SkillInstall_ClaudeProject_CreatesFile()
    {
        using var env = new TestEnv();
        var projectDir = Path.Combine(env.Root, "proj");
        Directory.CreateDirectory(projectDir);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[] { "skill", "install", "--target", "claude", "--scope", "project", "--project-dir", projectDir },
            sw, er);

        await Assert.That(exit).IsEqualTo(0);
        var expected = Path.Combine(projectDir, ".claude", "skills", "yt", "SKILL.md");
        await Assert.That(File.Exists(expected)).IsTrue();
    }

    [Test]
    public async Task SkillInstall_ClaudeExisting_NoForce_Fails()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        var first = await env.Invoke(new[] { "skill", "install", "--target", "claude" }, sw, er);
        await Assert.That(first).IsEqualTo(0);

        sw = new StringWriter();
        er = new StringWriter();
        var second = await env.Invoke(new[] { "skill", "install", "--target", "claude" }, sw, er);
        await Assert.That(second).IsEqualTo(2);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
    }

    [Test]
    public async Task SkillInstall_ClaudeExisting_Force_Overwrites()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "claude" }, sw, er);
        var path = Path.Combine(env.Root, "home", ".claude", "skills", "yt", "SKILL.md");
        File.WriteAllText(path, "STALE");

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "install", "--target", "claude", "--force" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        var actual = File.ReadAllText(path);
        await Assert.That(actual).Contains("# yt — Yandex Tracker CLI");
        await Assert.That(actual).DoesNotContain("STALE");
    }

    [Test]
    public async Task SkillInstall_CodexGlobal_CreatesFile()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[] { "skill", "install", "--target", "codex", "--scope", "global" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        var expected = Path.Combine(env.Root, "home", ".agents", "skills", "yt", "SKILL.md");
        await Assert.That(File.Exists(expected)).IsTrue();
        var content = File.ReadAllText(expected);
        await Assert.That(content).Contains("# yt — Yandex Tracker CLI");
        await Assert.That(content).Contains("<!-- yt-version: ");
        // Frontmatter присутствует — Codex также читает skill как полный SKILL.md.
        await Assert.That(content).Contains("---\nname: yt");
    }

    [Test]
    public async Task SkillInstall_CodexProject_CreatesFile()
    {
        using var env = new TestEnv();
        var projectDir = Path.Combine(env.Root, "proj");
        Directory.CreateDirectory(projectDir);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[] { "skill", "install", "--target", "codex", "--scope", "project", "--project-dir", projectDir },
            sw, er);

        await Assert.That(exit).IsEqualTo(0);
        var expected = Path.Combine(projectDir, ".agents", "skills", "yt", "SKILL.md");
        await Assert.That(File.Exists(expected)).IsTrue();
    }

    [Test]
    public async Task SkillInstall_CodexExisting_NoForce_Fails()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        var first = await env.Invoke(new[] { "skill", "install", "--target", "codex" }, sw, er);
        await Assert.That(first).IsEqualTo(0);

        sw = new StringWriter();
        er = new StringWriter();
        var second = await env.Invoke(new[] { "skill", "install", "--target", "codex" }, sw, er);
        await Assert.That(second).IsEqualTo(2);
    }

    [Test]
    public async Task SkillInstall_CodexExisting_Force_Overwrites()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "codex" }, sw, er);
        var path = Path.Combine(env.Root, "home", ".agents", "skills", "yt", "SKILL.md");
        File.WriteAllText(path, "STALE");

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "install", "--target", "codex", "--force" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(File.ReadAllText(path)).DoesNotContain("STALE");
    }

    [Test]
    public async Task SkillInstall_All_InstallsBoth()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "skill", "install" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        var claude = Path.Combine(env.Root, "home", ".claude", "skills", "yt", "SKILL.md");
        var codex = Path.Combine(env.Root, "home", ".agents", "skills", "yt", "SKILL.md");
        await Assert.That(File.Exists(claude)).IsTrue();
        await Assert.That(File.Exists(codex)).IsTrue();
    }
}
