namespace YandexTrackerCLI.Tests.Commands.Skill;

using System.Text.Json;
using TUnit.Core;

/// <summary>
/// End-to-end тесты команды <c>yt skill status</c>.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class SkillStatusCommandTests
{
    [Test]
    public async Task SkillStatus_NotInstalled_ReturnsNulls()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "skill", "status" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("claude").GetProperty("global").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
        await Assert.That(doc.RootElement.GetProperty("codex").GetProperty("global").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
        await Assert.That(doc.RootElement.GetProperty("any_outdated").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task SkillStatus_Installed_ReturnsPaths()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install" }, sw, er);

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "status" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        var current = doc.RootElement.GetProperty("current_version").GetString();
        await Assert.That(string.IsNullOrEmpty(current)).IsFalse();

        var claudeGlobal = doc.RootElement.GetProperty("claude").GetProperty("global");
        await Assert.That(claudeGlobal.ValueKind).IsNotEqualTo(JsonValueKind.Null);
        await Assert.That(claudeGlobal.GetProperty("up_to_date").GetBoolean()).IsTrue();
        await Assert.That(claudeGlobal.GetProperty("installed_version").GetString()).IsEqualTo(current);

        var codexGlobal = doc.RootElement.GetProperty("codex").GetProperty("global");
        await Assert.That(codexGlobal.ValueKind).IsNotEqualTo(JsonValueKind.Null);
        await Assert.That(codexGlobal.GetProperty("up_to_date").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task SkillStatus_OutdatedClaude_AnyOutdatedTrue()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "claude" }, sw, er);
        var path = Path.Combine(env.Root, "home", ".claude", "skills", "yt", "SKILL.md");
        // Подменяем version-маркер на «старую» версию.
        var raw = File.ReadAllText(path);
        var spoofed = System.Text.RegularExpressions.Regex.Replace(
            raw, @"<!--\s*yt-version:\s*[^\s>]+\s*-->", "<!-- yt-version: 0.0.1 -->");
        File.WriteAllText(path, spoofed);

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "status" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("any_outdated").GetBoolean()).IsTrue();
        await Assert.That(doc.RootElement
                .GetProperty("claude").GetProperty("global")
                .GetProperty("installed_version").GetString())
            .IsEqualTo("0.0.1");
        await Assert.That(doc.RootElement
                .GetProperty("claude").GetProperty("global")
                .GetProperty("up_to_date").GetBoolean())
            .IsFalse();
    }

    [Test]
    public async Task SkillStatus_AllUpToDate_AnyOutdatedFalse()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();
        await env.Invoke(new[] { "skill", "install" }, sw, er);

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "status" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("any_outdated").GetBoolean()).IsFalse();
    }
}
