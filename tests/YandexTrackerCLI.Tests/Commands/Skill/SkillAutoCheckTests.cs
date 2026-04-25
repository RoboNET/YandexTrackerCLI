namespace YandexTrackerCLI.Tests.Commands.Skill;

using System.Text.Json;
using System.Text.RegularExpressions;
using TUnit.Core;
using YandexTrackerCLI.Skill;

/// <summary>
/// Тесты <see cref="SkillAutoCheck"/>: TTY-prompt, non-TTY warning, never_prompt,
/// declined_for_versions, skip-by-flags.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class SkillAutoCheckTests
{
    private static void InstallStaleClaude(TestEnv env)
    {
        // Установим skill, потом подменим маркер версии на старую.
        var path = SkillPaths.Resolve(SkillTarget.Claude, SkillScope.Global, env.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var raw = EmbeddedSkill.ReadAll();
        var stale = Regex.Replace(raw, @"<!--\s*yt-version:\s*[^\s>]+\s*-->", "<!-- yt-version: 0.0.1 -->");
        File.WriteAllText(path, stale);
    }

    [Test]
    public async Task SkillCheck_OutdatedClaude_TtyPromptYes_RunsUpdate()
    {
        using var env = new TestEnv();
        env.Set("YT_SKILL_CHECK", null); // re-enable
        InstallStaleClaude(env);

        SkillAutoCheck.TestForceInteractive.Value = true;
        SkillAutoCheck.TestPromptReader.Value = () => "y";
        var sw = new StringWriter();
        SkillAutoCheck.TestStdout.Value = sw;
        try
        {
            SkillAutoCheck.RunIfNeeded(env.Root);
        }
        finally
        {
            SkillAutoCheck.TestForceInteractive.Value = null;
            SkillAutoCheck.TestPromptReader.Value = null;
            SkillAutoCheck.TestStdout.Value = null;
        }

        // After yes — skill is updated to current version.
        var path = SkillPaths.Resolve(SkillTarget.Claude, SkillScope.Global, env.Root);
        var content = File.ReadAllText(path);
        await Assert.That(content).DoesNotContain("yt-version: 0.0.1");
        await Assert.That(sw.ToString()).Contains("Обновлено");
    }

    [Test]
    public async Task SkillCheck_OutdatedClaude_TtyPromptNo_SavesDeclined()
    {
        using var env = new TestEnv();
        env.Set("YT_SKILL_CHECK", null);
        InstallStaleClaude(env);

        SkillAutoCheck.TestForceInteractive.Value = true;
        SkillAutoCheck.TestPromptReader.Value = () => "n";
        SkillAutoCheck.TestStdout.Value = new StringWriter();
        try
        {
            SkillAutoCheck.RunIfNeeded(env.Root);
        }
        finally
        {
            SkillAutoCheck.TestForceInteractive.Value = null;
            SkillAutoCheck.TestPromptReader.Value = null;
            SkillAutoCheck.TestStdout.Value = null;
        }

        var state = SkillPromptState.Load();
        await Assert.That(state.NeverPrompt).IsFalse();
        await Assert.That(state.DeclinedForVersions).Contains(EmbeddedSkill.GetVersion());
    }

    [Test]
    public async Task SkillCheck_OutdatedClaude_TtyPromptNever_SavesNever()
    {
        using var env = new TestEnv();
        env.Set("YT_SKILL_CHECK", null);
        InstallStaleClaude(env);

        SkillAutoCheck.TestForceInteractive.Value = true;
        SkillAutoCheck.TestPromptReader.Value = () => "never";
        SkillAutoCheck.TestStdout.Value = new StringWriter();
        try
        {
            SkillAutoCheck.RunIfNeeded(env.Root);
        }
        finally
        {
            SkillAutoCheck.TestForceInteractive.Value = null;
            SkillAutoCheck.TestPromptReader.Value = null;
            SkillAutoCheck.TestStdout.Value = null;
        }

        var state = SkillPromptState.Load();
        await Assert.That(state.NeverPrompt).IsTrue();
    }

    [Test]
    public async Task SkillCheck_NonTty_PrintsWarningOnce()
    {
        using var env = new TestEnv();
        env.Set("YT_SKILL_CHECK", null);
        InstallStaleClaude(env);

        SkillAutoCheck.TestForceInteractive.Value = false;

        var er1 = new StringWriter();
        SkillAutoCheck.TestStderr.Value = er1;
        try { SkillAutoCheck.RunIfNeeded(env.Root); }
        finally { SkillAutoCheck.TestStderr.Value = null; }

        var er2 = new StringWriter();
        SkillAutoCheck.TestStderr.Value = er2;
        try { SkillAutoCheck.RunIfNeeded(env.Root); }
        finally
        {
            SkillAutoCheck.TestStderr.Value = null;
            SkillAutoCheck.TestForceInteractive.Value = null;
        }

        // First call printed warning JSON; second — silent.
        await Assert.That(er1.ToString()).Contains("\"code\":\"skill_outdated\"");
        await Assert.That(er2.ToString()).IsEmpty();

        // Validate JSON.
        using var doc = JsonDocument.Parse(er1.ToString());
        await Assert.That(doc.RootElement.GetProperty("warning").GetProperty("code").GetString())
            .IsEqualTo("skill_outdated");
    }

    [Test]
    public async Task SkillCheck_NeverPrompt_Skipped()
    {
        using var env = new TestEnv();
        env.Set("YT_SKILL_CHECK", null);
        InstallStaleClaude(env);

        var state = new SkillPromptState { NeverPrompt = true };
        state.Save();

        SkillAutoCheck.TestForceInteractive.Value = true;
        var promptCalled = false;
        SkillAutoCheck.TestPromptReader.Value = () => { promptCalled = true; return ""; };
        var sw = new StringWriter();
        SkillAutoCheck.TestStdout.Value = sw;
        try { SkillAutoCheck.RunIfNeeded(env.Root); }
        finally
        {
            SkillAutoCheck.TestForceInteractive.Value = null;
            SkillAutoCheck.TestPromptReader.Value = null;
            SkillAutoCheck.TestStdout.Value = null;
        }

        await Assert.That(promptCalled).IsFalse();
        await Assert.That(sw.ToString()).IsEmpty();
    }

    [Test]
    public async Task SkillCheck_DeclinedForVersion_Skipped()
    {
        using var env = new TestEnv();
        env.Set("YT_SKILL_CHECK", null);
        InstallStaleClaude(env);

        var state = new SkillPromptState();
        state.DeclinedForVersions.Add(EmbeddedSkill.GetVersion());
        state.Save();

        SkillAutoCheck.TestForceInteractive.Value = true;
        var promptCalled = false;
        SkillAutoCheck.TestPromptReader.Value = () => { promptCalled = true; return ""; };
        SkillAutoCheck.TestStdout.Value = new StringWriter();
        try { SkillAutoCheck.RunIfNeeded(env.Root); }
        finally
        {
            SkillAutoCheck.TestForceInteractive.Value = null;
            SkillAutoCheck.TestPromptReader.Value = null;
            SkillAutoCheck.TestStdout.Value = null;
        }

        await Assert.That(promptCalled).IsFalse();
    }

    [Test]
    public async Task SkillCheck_DisabledByEnv_Skipped()
    {
        using var env = new TestEnv();
        env.Set("YT_SKILL_CHECK", "0");
        InstallStaleClaude(env);

        SkillAutoCheck.TestForceInteractive.Value = true;
        var promptCalled = false;
        SkillAutoCheck.TestPromptReader.Value = () => { promptCalled = true; return ""; };
        SkillAutoCheck.TestStdout.Value = new StringWriter();
        try { SkillAutoCheck.RunIfNeeded(env.Root); }
        finally
        {
            SkillAutoCheck.TestForceInteractive.Value = null;
            SkillAutoCheck.TestPromptReader.Value = null;
            SkillAutoCheck.TestStdout.Value = null;
        }

        await Assert.That(promptCalled).IsFalse();
    }

    [Test]
    public async Task SkillCheck_UpToDate_Silent()
    {
        using var env = new TestEnv();
        env.Set("YT_SKILL_CHECK", null);

        // Установим актуальный skill (без подмены версии).
        var path = SkillPaths.Resolve(SkillTarget.Claude, SkillScope.Global, env.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, EmbeddedSkill.ReadAll());

        SkillAutoCheck.TestForceInteractive.Value = true;
        var promptCalled = false;
        SkillAutoCheck.TestPromptReader.Value = () => { promptCalled = true; return ""; };
        var sw = new StringWriter();
        var er = new StringWriter();
        SkillAutoCheck.TestStdout.Value = sw;
        SkillAutoCheck.TestStderr.Value = er;
        try { SkillAutoCheck.RunIfNeeded(env.Root); }
        finally
        {
            SkillAutoCheck.TestForceInteractive.Value = null;
            SkillAutoCheck.TestPromptReader.Value = null;
            SkillAutoCheck.TestStdout.Value = null;
            SkillAutoCheck.TestStderr.Value = null;
        }

        await Assert.That(promptCalled).IsFalse();
        await Assert.That(sw.ToString()).IsEmpty();
        await Assert.That(er.ToString()).IsEmpty();
    }

    [Test]
    public async Task SkillCheck_ShouldSkipFromArgs_SkillCommand()
    {
        await Assert.That(SkillAutoCheck.ShouldSkipFromArgs(new[] { "skill", "status" })).IsTrue();
        await Assert.That(SkillAutoCheck.ShouldSkipFromArgs(new[] { "skill", "install" })).IsTrue();
        await Assert.That(SkillAutoCheck.ShouldSkipFromArgs(new[] { "user", "me" })).IsFalse();
    }

    [Test]
    public async Task SkillCheck_ShouldSkipFromArgs_VersionAndHelp()
    {
        await Assert.That(SkillAutoCheck.ShouldSkipFromArgs(new[] { "--version" })).IsTrue();
        await Assert.That(SkillAutoCheck.ShouldSkipFromArgs(new[] { "-v" })).IsTrue();
        await Assert.That(SkillAutoCheck.ShouldSkipFromArgs(new[] { "--help" })).IsTrue();
        await Assert.That(SkillAutoCheck.ShouldSkipFromArgs(new[] { "-h" })).IsTrue();
        await Assert.That(SkillAutoCheck.ShouldSkipFromArgs(new[] { "user", "me", "--no-skill-check" })).IsTrue();
    }
}
