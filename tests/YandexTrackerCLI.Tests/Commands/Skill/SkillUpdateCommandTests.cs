namespace YandexTrackerCLI.Tests.Commands.Skill;

using System.Text.Json;
using System.Text.RegularExpressions;
using TUnit.Core;

/// <summary>
/// End-to-end тесты команды <c>yt skill update</c>.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class SkillUpdateCommandTests
{
    private static void StaleVersion(string path)
    {
        var raw = File.ReadAllText(path);
        File.WriteAllText(path, Regex.Replace(raw, @"<!--\s*yt-version:\s*[^\s>]+\s*-->", "<!-- yt-version: 0.0.1 -->"));
    }

    [Test]
    public async Task SkillUpdate_NoInstallations_ReturnsEmptyMessage()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "skill", "update" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("updated").GetArrayLength()).IsEqualTo(0);
        await Assert.That(doc.RootElement.TryGetProperty("message", out _)).IsTrue();
    }

    [Test]
    public async Task SkillUpdate_OutdatedClaude_RewritesFile()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "claude" }, sw, er);
        var path = Path.Combine(env.Root, "home", ".claude", "skills", "yt", "SKILL.md");
        StaleVersion(path);

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "update", "--target", "claude" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        var content = File.ReadAllText(path);
        await Assert.That(content).DoesNotContain("yt-version: 0.0.1");

        using var doc = JsonDocument.Parse(sw.ToString());
        var arr = doc.RootElement.GetProperty("updated").EnumerateArray().ToArray();
        await Assert.That(arr.Length).IsEqualTo(1);
        await Assert.That(arr[0].GetProperty("from_version").GetString()).IsEqualTo("0.0.1");
        await Assert.That(arr[0].GetProperty("to_version").GetString()).IsNotEqualTo("0.0.1");
    }

    [Test]
    public async Task SkillUpdate_OutdatedCodex_RewritesFile()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "codex" }, sw, er);
        var path = Path.Combine(env.Root, "home", ".agents", "skills", "yt", "SKILL.md");
        StaleVersion(path);

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "update", "--target", "codex" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        var content = File.ReadAllText(path);
        await Assert.That(content).DoesNotContain("yt-version: 0.0.1");

        using var doc = JsonDocument.Parse(sw.ToString());
        var arr = doc.RootElement.GetProperty("updated").EnumerateArray().ToArray();
        await Assert.That(arr.Length).IsEqualTo(1);
        await Assert.That(arr[0].GetProperty("target").GetString()).IsEqualTo("codex");
        await Assert.That(arr[0].GetProperty("from_version").GetString()).IsEqualTo("0.0.1");
    }

    [Test]
    public async Task SkillUpdate_All_UpdatesBothOutdated()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install" }, sw, er);
        StaleVersion(Path.Combine(env.Root, "home", ".claude", "skills", "yt", "SKILL.md"));
        StaleVersion(Path.Combine(env.Root, "home", ".agents", "skills", "yt", "SKILL.md"));

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "update" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("updated").GetArrayLength()).IsEqualTo(2);
    }
}
