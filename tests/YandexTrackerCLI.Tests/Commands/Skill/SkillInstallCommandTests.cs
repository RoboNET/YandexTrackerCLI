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
    public async Task SkillInstall_AllGlobal_InstallsFour_SkipsCopilot()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        // `skill install` (default --target all --scope global) — 5 target'ов × 1 scope.
        // Copilot global → skipped (не error), остальные 4 устанавливаются.
        var exit = await env.Invoke(new[] { "skill", "install" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        var claude = Path.Combine(env.Root, "home", ".claude", "skills", "yt", "SKILL.md");
        var codex = Path.Combine(env.Root, "home", ".agents", "skills", "yt", "SKILL.md");
        var gemini = Path.Combine(env.Root, "home", ".gemini", "skills", "yt", "SKILL.md");
        var cursor = Path.Combine(env.Root, "home", ".cursor", "rules", "yt.mdc");
        await Assert.That(File.Exists(claude)).IsTrue();
        await Assert.That(File.Exists(codex)).IsTrue();
        await Assert.That(File.Exists(gemini)).IsTrue();
        await Assert.That(File.Exists(cursor)).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        var installed = doc.RootElement.GetProperty("installed").EnumerateArray().ToArray();
        await Assert.That(installed.Length).IsEqualTo(4);

        var skipped = doc.RootElement.GetProperty("skipped").EnumerateArray().ToArray();
        await Assert.That(skipped.Length).IsEqualTo(1);
        await Assert.That(skipped[0].GetProperty("target").GetString()).IsEqualTo("copilot");
        await Assert.That(skipped[0].GetProperty("scope").GetString()).IsEqualTo("global");
        await Assert.That(skipped[0].GetProperty("skipped").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task SkillInstall_AllProject_InstallsFive()
    {
        using var env = new TestEnv();
        var projectDir = Path.Combine(env.Root, "proj");
        Directory.CreateDirectory(projectDir);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[] { "skill", "install", "--scope", "project", "--project-dir", projectDir },
            sw, er);
        await Assert.That(exit).IsEqualTo(0);

        await Assert.That(File.Exists(Path.Combine(projectDir, ".claude", "skills", "yt", "SKILL.md"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(projectDir, ".agents", "skills", "yt", "SKILL.md"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(projectDir, ".gemini", "skills", "yt", "SKILL.md"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(projectDir, ".cursor", "rules", "yt.mdc"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(projectDir, ".github", "instructions", "yt.instructions.md"))).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        var installed = doc.RootElement.GetProperty("installed").EnumerateArray().ToArray();
        await Assert.That(installed.Length).IsEqualTo(5);
        await Assert.That(doc.RootElement.GetProperty("skipped").GetArrayLength()).IsEqualTo(0);
    }
}
