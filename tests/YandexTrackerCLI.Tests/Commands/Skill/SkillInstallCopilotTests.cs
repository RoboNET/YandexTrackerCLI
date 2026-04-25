namespace YandexTrackerCLI.Tests.Commands.Skill;

using System.Text.Json;
using TUnit.Core;

/// <summary>
/// End-to-end тесты команды <c>yt skill install --target copilot</c>.
/// Copilot использует формат <c>.instructions.md</c> с frontmatter <c>applyTo: "**"</c>,
/// поддерживает только project-scope; global-scope для Copilot — skipped (не error).
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class SkillInstallCopilotTests
{
    [Test]
    public async Task SkillInstall_CopilotProject_CreatesInstructionsFile()
    {
        using var env = new TestEnv();
        var projectDir = Path.Combine(env.Root, "proj");
        Directory.CreateDirectory(projectDir);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[] { "skill", "install", "--target", "copilot", "--scope", "project", "--project-dir", projectDir },
            sw, er);

        await Assert.That(exit).IsEqualTo(0);
        var expected = Path.Combine(projectDir, ".github", "instructions", "yt.instructions.md");
        await Assert.That(File.Exists(expected)).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        var arr = doc.RootElement.GetProperty("installed").EnumerateArray().ToArray();
        await Assert.That(arr.Length).IsEqualTo(1);
        await Assert.That(arr[0].GetProperty("path").GetString()).IsEqualTo(expected);
        await Assert.That(arr[0].GetProperty("target").GetString()).IsEqualTo("copilot");
        await Assert.That(arr[0].GetProperty("scope").GetString()).IsEqualTo("project");
    }

    [Test]
    public async Task SkillInstall_CopilotGlobal_Skipped()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        // Запрос copilot+global должен НЕ упасть, а вернуть skipped-запись.
        var exit = await env.Invoke(
            new[] { "skill", "install", "--target", "copilot", "--scope", "global" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        var installed = doc.RootElement.GetProperty("installed").EnumerateArray().ToArray();
        await Assert.That(installed.Length).IsEqualTo(0);

        var skipped = doc.RootElement.GetProperty("skipped").EnumerateArray().ToArray();
        await Assert.That(skipped.Length).IsEqualTo(1);
        await Assert.That(skipped[0].GetProperty("target").GetString()).IsEqualTo("copilot");
        await Assert.That(skipped[0].GetProperty("scope").GetString()).IsEqualTo("global");
        await Assert.That(skipped[0].GetProperty("skipped").GetBoolean()).IsTrue();
        await Assert.That(skipped[0].GetProperty("reason").GetString())
            .Contains("Copilot does not support global scope");
    }

    [Test]
    public async Task SkillInstall_CopilotContent_HasApplyTo()
    {
        using var env = new TestEnv();
        var projectDir = Path.Combine(env.Root, "proj");
        Directory.CreateDirectory(projectDir);
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(
            new[] { "skill", "install", "--target", "copilot", "--scope", "project", "--project-dir", projectDir },
            sw, er);
        var path = Path.Combine(projectDir, ".github", "instructions", "yt.instructions.md");
        var content = File.ReadAllText(path);

        // Copilot frontmatter содержит applyTo: "**".
        await Assert.That(content).StartsWith("---\n");
        await Assert.That(content).Contains("applyTo: \"**\"");
        await Assert.That(content).Contains("description:");
        // Frontmatter SKILL.md (с name:) НЕ должен присутствовать в Copilot-варианте.
        await Assert.That(content).DoesNotContain("name: yt");
    }

    [Test]
    public async Task SkillInstall_CopilotContent_HasVersionMarker()
    {
        using var env = new TestEnv();
        var projectDir = Path.Combine(env.Root, "proj");
        Directory.CreateDirectory(projectDir);
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(
            new[] { "skill", "install", "--target", "copilot", "--scope", "project", "--project-dir", projectDir },
            sw, er);
        var content = File.ReadAllText(Path.Combine(projectDir, ".github", "instructions", "yt.instructions.md"));

        await Assert.That(content).Contains("<!-- yt-version: ");
        await Assert.That(content).DoesNotContain("{VERSION}");
        await Assert.That(content).Contains("# yt — Yandex Tracker CLI");
    }

    [Test]
    public async Task SkillUninstall_Copilot_RemovesFile()
    {
        using var env = new TestEnv();
        var projectDir = Path.Combine(env.Root, "proj");
        Directory.CreateDirectory(projectDir);
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(
            new[] { "skill", "install", "--target", "copilot", "--scope", "project", "--project-dir", projectDir },
            sw, er);
        var path = Path.Combine(projectDir, ".github", "instructions", "yt.instructions.md");
        await Assert.That(File.Exists(path)).IsTrue();

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "skill", "uninstall", "--target", "copilot", "--scope", "project", "--project-dir", projectDir },
            sw, er);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(File.Exists(path)).IsFalse();
    }

    [Test]
    public async Task SkillStatus_Copilot_ReportsInstalledAndGlobalNull()
    {
        using var env = new TestEnv();
        var projectDir = Path.Combine(env.Root, "proj");
        Directory.CreateDirectory(projectDir);
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(
            new[] { "skill", "install", "--target", "copilot", "--scope", "project", "--project-dir", projectDir },
            sw, er);

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "skill", "status", "--project-dir", projectDir }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        var copilot = doc.RootElement.GetProperty("copilot");

        // Copilot.global всегда null — Copilot не поддерживает global scope.
        await Assert.That(copilot.GetProperty("global").ValueKind).IsEqualTo(JsonValueKind.Null);

        var copilotProject = copilot.GetProperty("project");
        await Assert.That(copilotProject.ValueKind).IsNotEqualTo(JsonValueKind.Null);
        await Assert.That(copilotProject.GetProperty("up_to_date").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task SkillUpdate_OutdatedCopilot_RewritesFile()
    {
        using var env = new TestEnv();
        var projectDir = Path.Combine(env.Root, "proj");
        Directory.CreateDirectory(projectDir);
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(
            new[] { "skill", "install", "--target", "copilot", "--scope", "project", "--project-dir", projectDir },
            sw, er);
        var path = Path.Combine(projectDir, ".github", "instructions", "yt.instructions.md");
        var raw = File.ReadAllText(path);
        File.WriteAllText(path, System.Text.RegularExpressions.Regex.Replace(
            raw, @"<!--\s*yt-version:\s*[^\s>]+\s*-->", "<!-- yt-version: 0.0.1 -->"));

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "skill", "update", "--target", "copilot", "--scope", "project", "--project-dir", projectDir },
            sw, er);
        await Assert.That(exit).IsEqualTo(0);

        var content = File.ReadAllText(path);
        await Assert.That(content).DoesNotContain("yt-version: 0.0.1");
        // После update copilot-frontmatter должен сохраниться.
        await Assert.That(content).Contains("applyTo: \"**\"");
    }
}
