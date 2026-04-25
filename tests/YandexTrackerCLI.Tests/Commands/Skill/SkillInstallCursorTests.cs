namespace YandexTrackerCLI.Tests.Commands.Skill;

using System.Text.Json;
using TUnit.Core;

/// <summary>
/// End-to-end тесты команды <c>yt skill install --target cursor</c>.
/// Cursor использует свой формат <c>.mdc</c> с frontmatter (<c>description</c>, <c>globs</c>, <c>alwaysApply</c>),
/// файл лежит прямо в <c>~/.cursor/rules/yt.mdc</c> (без <c>yt/</c> подкаталога).
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class SkillInstallCursorTests
{
    [Test]
    public async Task SkillInstall_CursorGlobal_CreatesMdcFile()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[] { "skill", "install", "--target", "cursor", "--scope", "global" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        var expected = Path.Combine(env.Root, "home", ".cursor", "rules", "yt.mdc");
        await Assert.That(File.Exists(expected)).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        var arr = doc.RootElement.GetProperty("installed").EnumerateArray().ToArray();
        await Assert.That(arr.Length).IsEqualTo(1);
        await Assert.That(arr[0].GetProperty("path").GetString()).IsEqualTo(expected);
        await Assert.That(arr[0].GetProperty("target").GetString()).IsEqualTo("cursor");
    }

    [Test]
    public async Task SkillInstall_CursorProject_CreatesMdcFile()
    {
        using var env = new TestEnv();
        var projectDir = Path.Combine(env.Root, "proj");
        Directory.CreateDirectory(projectDir);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[] { "skill", "install", "--target", "cursor", "--scope", "project", "--project-dir", projectDir },
            sw, er);

        await Assert.That(exit).IsEqualTo(0);
        var expected = Path.Combine(projectDir, ".cursor", "rules", "yt.mdc");
        await Assert.That(File.Exists(expected)).IsTrue();
    }

    [Test]
    public async Task SkillInstall_CursorContent_HasMdcFrontmatter()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "cursor" }, sw, er);
        var path = Path.Combine(env.Root, "home", ".cursor", "rules", "yt.mdc");
        var content = File.ReadAllText(path);

        // Cursor frontmatter обязательно начинается с --- и содержит 3 поля.
        await Assert.That(content).StartsWith("---\n");
        await Assert.That(content).Contains("description:");
        await Assert.That(content).Contains("globs:");
        await Assert.That(content).Contains("alwaysApply: false");

        // Frontmatter SKILL.md (с name:) НЕ должен присутствовать в Cursor-варианте.
        await Assert.That(content).DoesNotContain("name: yt");
    }

    [Test]
    public async Task SkillInstall_CursorContent_HasVersionMarker()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "cursor" }, sw, er);
        var path = Path.Combine(env.Root, "home", ".cursor", "rules", "yt.mdc");
        var content = File.ReadAllText(path);

        // Маркер версии присутствует — иначе status/update не определят actual version.
        await Assert.That(content).Contains("<!-- yt-version: ");
        await Assert.That(content).DoesNotContain("{VERSION}");

        // И само тело skill'а присутствует (после маркера).
        await Assert.That(content).Contains("# yt — Yandex Tracker CLI");
    }

    [Test]
    public async Task SkillInstall_CursorExisting_NoForce_Fails()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        var first = await env.Invoke(new[] { "skill", "install", "--target", "cursor" }, sw, er);
        await Assert.That(first).IsEqualTo(0);

        sw = new StringWriter();
        er = new StringWriter();
        var second = await env.Invoke(new[] { "skill", "install", "--target", "cursor" }, sw, er);
        await Assert.That(second).IsEqualTo(2);
    }

    [Test]
    public async Task SkillUninstall_Cursor_RemovesMdcFile()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "cursor" }, sw, er);
        var path = Path.Combine(env.Root, "home", ".cursor", "rules", "yt.mdc");
        await Assert.That(File.Exists(path)).IsTrue();

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "uninstall", "--target", "cursor" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(File.Exists(path)).IsFalse();
    }

    [Test]
    public async Task SkillStatus_Cursor_ReportsInstalled()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "cursor" }, sw, er);

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "status" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        var cursorGlobal = doc.RootElement.GetProperty("cursor").GetProperty("global");
        await Assert.That(cursorGlobal.ValueKind).IsNotEqualTo(JsonValueKind.Null);
        await Assert.That(cursorGlobal.GetProperty("up_to_date").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task SkillUpdate_OutdatedCursor_RewritesFile()
    {
        using var env = new TestEnv();
        var sw = new StringWriter();
        var er = new StringWriter();

        await env.Invoke(new[] { "skill", "install", "--target", "cursor" }, sw, er);
        var path = Path.Combine(env.Root, "home", ".cursor", "rules", "yt.mdc");
        var raw = File.ReadAllText(path);
        File.WriteAllText(path, System.Text.RegularExpressions.Regex.Replace(
            raw, @"<!--\s*yt-version:\s*[^\s>]+\s*-->", "<!-- yt-version: 0.0.1 -->"));

        sw = new StringWriter();
        er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "update", "--target", "cursor" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        var content = File.ReadAllText(path);
        await Assert.That(content).DoesNotContain("yt-version: 0.0.1");
        // После update cursor-frontmatter должен сохраниться (а не превратиться обратно в SKILL.md frontmatter).
        await Assert.That(content).Contains("alwaysApply: false");
        await Assert.That(content).DoesNotContain("name: yt");
    }
}
