namespace YandexTrackerCLI.Tests.Commands.Skill;

using System.Text.Json;
using TUnit.Core;

/// <summary>
/// End-to-end тесты команды <c>yt skill install --target gemini</c>.
/// Контент Gemini — полный SKILL.md as-is (как и Claude/Codex), путь — <c>~/.gemini/skills/yt/SKILL.md</c>.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class SkillInstallGeminiTests
{
    [Test]
    public async Task SkillInstall_GeminiGlobal_CreatesFile()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[] { "skill", "install", "--target", "gemini", "--scope", "global" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        var expected = Path.Combine(env.Root, "home", ".gemini", "skills", "yt", "SKILL.md");
        await Assert.That(File.Exists(expected)).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        var arr = doc.RootElement.GetProperty("installed").EnumerateArray().ToArray();
        await Assert.That(arr.Length).IsEqualTo(1);
        await Assert.That(arr[0].GetProperty("path").GetString()).IsEqualTo(expected);
        await Assert.That(arr[0].GetProperty("target").GetString()).IsEqualTo("gemini");
        await Assert.That(arr[0].GetProperty("scope").GetString()).IsEqualTo("global");
    }

    [Test]
    public async Task SkillInstall_GeminiProject_CreatesFile()
    {
        using var env = new TestEnv();
        var projectDir = Path.Combine(env.Root, "proj");
        Directory.CreateDirectory(projectDir);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[] { "skill", "install", "--target", "gemini", "--scope", "project", "--project-dir", projectDir },
            sw, er);

        await Assert.That(exit).IsEqualTo(0);
        var expected = Path.Combine(projectDir, ".gemini", "skills", "yt", "SKILL.md");
        await Assert.That(File.Exists(expected)).IsTrue();
    }

    [Test]
    public async Task SkillInstall_GeminiContent_MatchesClaude()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "claude" }, sw, er);
        await env.Invoke(new[] { "skill", "install", "--target", "gemini" }, sw, er);

        var claude = File.ReadAllText(Path.Combine(env.Root, "home", ".claude", "skills", "yt", "SKILL.md"));
        var gemini = File.ReadAllText(Path.Combine(env.Root, "home", ".gemini", "skills", "yt", "SKILL.md"));
        await Assert.That(gemini).IsEqualTo(claude);
        await Assert.That(gemini).Contains("---\nname: yt");
        await Assert.That(gemini).Contains("<!-- yt-version: ");
        await Assert.That(gemini).Contains("# yt — Yandex Tracker CLI");
    }

    [Test]
    public async Task SkillInstall_GeminiExisting_NoForce_Fails()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        var first = await env.Invoke(new[] { "skill", "install", "--target", "gemini" }, sw, er);
        await Assert.That(first).IsEqualTo(0);

        sw = new StringWriter();
        er = new StringWriter();
        var second = await env.Invoke(new[] { "skill", "install", "--target", "gemini" }, sw, er);
        await Assert.That(second).IsEqualTo(2);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
    }

    [Test]
    public async Task SkillInstall_GeminiExisting_Force_Overwrites()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "gemini" }, sw, er);
        var path = Path.Combine(env.Root, "home", ".gemini", "skills", "yt", "SKILL.md");
        File.WriteAllText(path, "STALE");

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "install", "--target", "gemini", "--force" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(File.ReadAllText(path)).DoesNotContain("STALE");
    }

    [Test]
    public async Task SkillUninstall_Gemini_RemovesFile()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "gemini" }, sw, er);
        var path = Path.Combine(env.Root, "home", ".gemini", "skills", "yt", "SKILL.md");
        await Assert.That(File.Exists(path)).IsTrue();

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "uninstall", "--target", "gemini" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(File.Exists(path)).IsFalse();
    }

    [Test]
    public async Task SkillStatus_Gemini_ReportsInstalled()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "gemini" }, sw, er);

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "status" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        var geminiGlobal = doc.RootElement.GetProperty("gemini").GetProperty("global");
        await Assert.That(geminiGlobal.ValueKind).IsNotEqualTo(JsonValueKind.Null);
        await Assert.That(geminiGlobal.GetProperty("up_to_date").GetBoolean()).IsTrue();
        await Assert.That(doc.RootElement.GetProperty("any_outdated").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task SkillUpdate_OutdatedGemini_RewritesFile()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "gemini" }, sw, er);
        var path = Path.Combine(env.Root, "home", ".gemini", "skills", "yt", "SKILL.md");
        var raw = File.ReadAllText(path);
        File.WriteAllText(path, System.Text.RegularExpressions.Regex.Replace(
            raw, @"<!--\s*yt-version:\s*[^\s>]+\s*-->", "<!-- yt-version: 0.0.1 -->"));

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "update", "--target", "gemini" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        var content = File.ReadAllText(path);
        await Assert.That(content).DoesNotContain("yt-version: 0.0.1");

        using var doc = JsonDocument.Parse(sw.ToString());
        var arr = doc.RootElement.GetProperty("updated").EnumerateArray().ToArray();
        await Assert.That(arr.Length).IsEqualTo(1);
        await Assert.That(arr[0].GetProperty("target").GetString()).IsEqualTo("gemini");
        await Assert.That(arr[0].GetProperty("from_version").GetString()).IsEqualTo("0.0.1");
    }
}
