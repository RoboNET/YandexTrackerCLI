namespace YandexTrackerCLI.Tests.Commands.Skill;

using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Skill;

/// <summary>
/// End-to-end тесты интерактивного режима <c>yt skill install</c>: подмена
/// <see cref="ISkillInstallPrompt"/> через <see cref="SkillInstallPrompt.TestOverride"/>,
/// + проверка скипа интерактива при <c>--no-prompt</c>, явных <c>--target</c>
/// и в non-TTY (через <see cref="SkillInstallCommandHelpers.TestForceInteractive"/>).
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class SkillInstallInteractiveTests
{
    /// <summary>
    /// Фейковый prompt с пред-заданными ответами и счётчиками вызовов — позволяет
    /// проверить что (a) интерактивные методы вызывались только когда ожидалось,
    /// (b) в каком порядке и (c) с какими параметрами.
    /// </summary>
    private sealed class FakePrompt : ISkillInstallPrompt
    {
        public int TargetCalls { get; private set; }
        public int ScopeCalls { get; private set; }
        public int OverwriteCalls { get; private set; }
        public IReadOnlyList<SkillTarget> LastDetected { get; private set; } = Array.Empty<SkillTarget>();
        public IReadOnlyList<string> LastExisting { get; private set; } = Array.Empty<string>();

        public SkillTarget[] TargetsToReturn { get; init; } = Array.Empty<SkillTarget>();
        public SkillScope ScopeToReturn { get; init; } = SkillScope.Global;
        public bool OverwriteToReturn { get; init; }

        public SkillTarget[] PromptTargets(IReadOnlyList<SkillTarget> all, IReadOnlyList<SkillTarget> detected)
        {
            TargetCalls++;
            LastDetected = detected;
            return TargetsToReturn;
        }

        public SkillScope PromptScope()
        {
            ScopeCalls++;
            return ScopeToReturn;
        }

        public bool PromptOverwrite(IReadOnlyList<string> existingPaths)
        {
            OverwriteCalls++;
            LastExisting = existingPaths;
            return OverwriteToReturn;
        }
    }

    /// <summary>
    /// В non-TTY (TestForceInteractive=false) даже без флагов prompt НЕ должен запускаться;
    /// используются дефолты <c>target=all, scope=global</c>.
    /// </summary>
    [Test]
    public async Task SkillInstall_NonTty_NoPrompt()
    {
        using var env = new TestEnv();
        var fake = new FakePrompt();
        SkillInstallPrompt.TestOverride.Value = fake;
        SkillInstallCommandHelpers.TestForceInteractive.Value = false;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "install" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(fake.TargetCalls).IsEqualTo(0);
        await Assert.That(fake.ScopeCalls).IsEqualTo(0);
        await Assert.That(fake.OverwriteCalls).IsEqualTo(0);

        // Сработал старый flow: 4 установлено (Claude/Codex/Gemini/Cursor) + Copilot пропущен.
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("installed").GetArrayLength()).IsEqualTo(4);
        await Assert.That(doc.RootElement.GetProperty("skipped").GetArrayLength()).IsEqualTo(1);
    }

    /// <summary>
    /// Флаг <c>--no-prompt</c> явно отключает интерактив даже в TTY.
    /// </summary>
    [Test]
    public async Task SkillInstall_NoPromptFlag_BypassesInteractive()
    {
        using var env = new TestEnv();
        var fake = new FakePrompt();
        SkillInstallPrompt.TestOverride.Value = fake;
        SkillInstallCommandHelpers.TestForceInteractive.Value = true; // TTY есть...

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "install", "--no-prompt" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(fake.TargetCalls).IsEqualTo(0);
        await Assert.That(fake.ScopeCalls).IsEqualTo(0);
    }

    /// <summary>
    /// Передан <c>--target claude</c> → prompt НЕ запускается, ставится только Claude.
    /// </summary>
    [Test]
    public async Task SkillInstall_TargetFlag_BypassesInteractive()
    {
        using var env = new TestEnv();
        var fake = new FakePrompt();
        SkillInstallPrompt.TestOverride.Value = fake;
        SkillInstallCommandHelpers.TestForceInteractive.Value = true;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "skill", "install", "--target", "claude" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(fake.TargetCalls).IsEqualTo(0);

        var path = Path.Combine(env.Root, "home", ".claude", "skills", "yt", "SKILL.md");
        await Assert.That(File.Exists(path)).IsTrue();
    }

    /// <summary>
    /// Передан <c>--scope project</c> → prompt НЕ запускается.
    /// </summary>
    [Test]
    public async Task SkillInstall_ScopeFlag_BypassesInteractive()
    {
        using var env = new TestEnv();
        var fake = new FakePrompt();
        SkillInstallPrompt.TestOverride.Value = fake;
        SkillInstallCommandHelpers.TestForceInteractive.Value = true;
        var projectDir = Path.Combine(env.Root, "proj");
        Directory.CreateDirectory(projectDir);

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "skill", "install", "--scope", "project", "--project-dir", projectDir }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(fake.TargetCalls).IsEqualTo(0);
    }

    /// <summary>
    /// TTY + нет флагов → prompt запускается, ставит выбранные target'ы в выбранный scope.
    /// </summary>
    [Test]
    public async Task SkillInstall_Interactive_PromptsAndInstallsSelected()
    {
        using var env = new TestEnv();
        var fake = new FakePrompt
        {
            TargetsToReturn = new[] { SkillTarget.Claude, SkillTarget.Gemini },
            ScopeToReturn = SkillScope.Global,
        };
        SkillInstallPrompt.TestOverride.Value = fake;
        SkillInstallCommandHelpers.TestForceInteractive.Value = true;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "install" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(fake.TargetCalls).IsEqualTo(1);
        await Assert.That(fake.ScopeCalls).IsEqualTo(1);
        // Перезаписи нет (свежий env), overwrite-prompt не вызывается.
        await Assert.That(fake.OverwriteCalls).IsEqualTo(0);

        await Assert.That(File.Exists(Path.Combine(env.Root, "home", ".claude", "skills", "yt", "SKILL.md"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(env.Root, "home", ".gemini", "skills", "yt", "SKILL.md"))).IsTrue();
        // Codex НЕ должен быть установлен.
        await Assert.That(File.Exists(Path.Combine(env.Root, "home", ".agents", "skills", "yt", "SKILL.md"))).IsFalse();
    }

    /// <summary>
    /// Если файл уже существует, prompt спрашивает про overwrite. Ответ "yes" → перезаписывает.
    /// </summary>
    [Test]
    public async Task SkillInstall_Interactive_ExistingFile_OverwritePromptYes_Overwrites()
    {
        using var env = new TestEnv();

        // Pre-install Claude в STALE состоянии.
        var path = Path.Combine(env.Root, "home", ".claude", "skills", "yt", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "STALE-CONTENT");

        var fake = new FakePrompt
        {
            TargetsToReturn = new[] { SkillTarget.Claude },
            ScopeToReturn = SkillScope.Global,
            OverwriteToReturn = true,
        };
        SkillInstallPrompt.TestOverride.Value = fake;
        SkillInstallCommandHelpers.TestForceInteractive.Value = true;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "install" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(fake.OverwriteCalls).IsEqualTo(1);
        await Assert.That(fake.LastExisting).Contains(path);
        await Assert.That(File.ReadAllText(path)).DoesNotContain("STALE-CONTENT");
    }

    /// <summary>
    /// Если файл уже существует, и пользователь говорит "no" на overwrite — конкретный
    /// target пропускается (записывается в <c>skipped</c>), exit-code 0, без TrackerException.
    /// </summary>
    [Test]
    public async Task SkillInstall_Interactive_ExistingFile_OverwritePromptNo_SkipsThatTarget()
    {
        using var env = new TestEnv();
        var existingPath = Path.Combine(env.Root, "home", ".claude", "skills", "yt", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
        File.WriteAllText(existingPath, "STALE-CONTENT");

        var fake = new FakePrompt
        {
            // Выбраны Claude (existing) + Gemini (new).
            TargetsToReturn = new[] { SkillTarget.Claude, SkillTarget.Gemini },
            ScopeToReturn = SkillScope.Global,
            OverwriteToReturn = false, // пользователь отказался.
        };
        SkillInstallPrompt.TestOverride.Value = fake;
        SkillInstallCommandHelpers.TestForceInteractive.Value = true;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "install" }, sw, er);

        // Команда не должна упасть: Claude пропущен, Gemini установлен.
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(File.ReadAllText(existingPath)).Contains("STALE-CONTENT");

        var geminiPath = Path.Combine(env.Root, "home", ".gemini", "skills", "yt", "SKILL.md");
        await Assert.That(File.Exists(geminiPath)).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("installed").GetArrayLength()).IsEqualTo(1);
        await Assert.That(doc.RootElement.GetProperty("skipped").GetArrayLength()).IsEqualTo(1);
    }

    /// <summary>
    /// Если пользователь выбирает "0" (ничего) — устанавливается 0, без ошибки.
    /// </summary>
    [Test]
    public async Task SkillInstall_Interactive_NoTargetsSelected_NoOp()
    {
        using var env = new TestEnv();
        var fake = new FakePrompt
        {
            TargetsToReturn = Array.Empty<SkillTarget>(),
        };
        SkillInstallPrompt.TestOverride.Value = fake;
        SkillInstallCommandHelpers.TestForceInteractive.Value = true;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "skill", "install" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(fake.TargetCalls).IsEqualTo(1);
        // Scope не запрашивается, если ничего не выбрано.
        await Assert.That(fake.ScopeCalls).IsEqualTo(0);

        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("installed").GetArrayLength()).IsEqualTo(0);
    }

    /// <summary>
    /// Detector видит пред-созданный <c>~/.claude</c>: prompt получает Claude как detected.
    /// </summary>
    [Test]
    public async Task SkillInstall_Interactive_DetectsExistingClaudeDir()
    {
        using var env = new TestEnv();
        Directory.CreateDirectory(Path.Combine(env.Root, "home", ".claude"));

        var fake = new FakePrompt
        {
            TargetsToReturn = new[] { SkillTarget.Claude },
        };
        SkillInstallPrompt.TestOverride.Value = fake;
        SkillInstallCommandHelpers.TestForceInteractive.Value = true;

        var sw = new StringWriter();
        var er = new StringWriter();
        await env.Invoke(new[] { "skill", "install" }, sw, er);

        await Assert.That(fake.LastDetected).Contains(SkillTarget.Claude);
        await Assert.That(fake.LastDetected).DoesNotContain(SkillTarget.Codex);
    }

    /// <summary>
    /// Test для <see cref="ConsoleSkillInstallPrompt.ParseTargetsInput"/>: разные форматы.
    /// </summary>
    [Test]
    public async Task ParseTargetsInput_VariousFormats()
    {
        var all = new[]
        {
            SkillTarget.Claude, SkillTarget.Codex, SkillTarget.Gemini,
            SkillTarget.Cursor, SkillTarget.Copilot,
        };
        var detected = new[] { SkillTarget.Claude, SkillTarget.Gemini };

        // Empty → detected.
        var r1 = ConsoleSkillInstallPrompt.ParseTargetsInput("", all, detected);
        await Assert.That(r1).IsEquivalentTo(detected);

        // Empty + no detected → all.
        var r1a = ConsoleSkillInstallPrompt.ParseTargetsInput("", all, Array.Empty<SkillTarget>());
        await Assert.That(r1a).IsEquivalentTo(all);

        // 'a' → all.
        var r2 = ConsoleSkillInstallPrompt.ParseTargetsInput("a", all, detected);
        await Assert.That(r2).IsEquivalentTo(all);

        // '0' → empty.
        var r3 = ConsoleSkillInstallPrompt.ParseTargetsInput("0", all, detected);
        await Assert.That(r3.Length).IsEqualTo(0);

        // Numbers.
        var r4 = ConsoleSkillInstallPrompt.ParseTargetsInput("1,3", all, detected);
        await Assert.That(r4).IsEquivalentTo(new[] { SkillTarget.Claude, SkillTarget.Gemini });

        // Names.
        var r5 = ConsoleSkillInstallPrompt.ParseTargetsInput("claude, gemini", all, detected);
        await Assert.That(r5).IsEquivalentTo(new[] { SkillTarget.Claude, SkillTarget.Gemini });

        // Mixed: name + number.
        var r6 = ConsoleSkillInstallPrompt.ParseTargetsInput("claude 5", all, detected);
        await Assert.That(r6).IsEquivalentTo(new[] { SkillTarget.Claude, SkillTarget.Copilot });

        // Garbage → fallback to detected.
        var r7 = ConsoleSkillInstallPrompt.ParseTargetsInput("xyz", all, detected);
        await Assert.That(r7).IsEquivalentTo(detected);
    }

    /// <summary>
    /// Test для <see cref="ConsoleSkillInstallPrompt.ParseScopeInput"/>: дефолт + варианты.
    /// </summary>
    [Test]
    public async Task ParseScopeInput_VariousFormats()
    {
        await Assert.That(ConsoleSkillInstallPrompt.ParseScopeInput("")).IsEqualTo(SkillScope.Global);
        await Assert.That(ConsoleSkillInstallPrompt.ParseScopeInput("1")).IsEqualTo(SkillScope.Global);
        await Assert.That(ConsoleSkillInstallPrompt.ParseScopeInput("global")).IsEqualTo(SkillScope.Global);
        await Assert.That(ConsoleSkillInstallPrompt.ParseScopeInput("g")).IsEqualTo(SkillScope.Global);
        await Assert.That(ConsoleSkillInstallPrompt.ParseScopeInput("2")).IsEqualTo(SkillScope.Project);
        await Assert.That(ConsoleSkillInstallPrompt.ParseScopeInput("project")).IsEqualTo(SkillScope.Project);
        await Assert.That(ConsoleSkillInstallPrompt.ParseScopeInput("p")).IsEqualTo(SkillScope.Project);
    }

    /// <summary>
    /// Воспроизводит Windows-runner сценарий: temp-каталог свеже-создан, никаких
    /// промежуточных <c>.gemini/skills/yt/</c> не существует. <see cref="SkillManager.Install"/>
    /// должен сам создать всю иерархию (через <see cref="SkillManager.EnsureParent"/>).
    /// </summary>
    [Test]
    public async Task SkillInstall_FreshHomeWithoutAnyDirs_CreatesParents()
    {
        using var env = new TestEnv();

        // Дополнительно убедимся, что home существует, но НИЧЕГО внутри нет
        // (как на чистом Windows-runner'е).
        var home = Path.Combine(env.Root, "home");
        await Assert.That(Directory.Exists(home)).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(home, ".gemini"))).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(home, ".agents"))).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(home, ".claude"))).IsFalse();

        var sw = new StringWriter();
        var er = new StringWriter();
        // Все 4 global target'а + Copilot (skipped).
        var exit = await env.Invoke(new[] { "skill", "install", "--no-prompt" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(home, ".claude", "skills", "yt", "SKILL.md"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(home, ".agents", "skills", "yt", "SKILL.md"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(home, ".gemini", "skills", "yt", "SKILL.md"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(home, ".cursor", "rules", "yt.mdc"))).IsTrue();
    }

    /// <summary>
    /// Аналог Windows-runner: home даже не создан вообще (TestEnv создаёт его сам,
    /// но мы удалим дочерние подкаталоги). Гарантия что rerun работает.
    /// </summary>
    [Test]
    public async Task SkillInstall_HomeWithoutChildrenAfterDelete_RecreatesParents()
    {
        using var env = new TestEnv();
        var home = Path.Combine(env.Root, "home");

        // Принудительно зачистим возможные следы.
        foreach (var sub in new[] { ".claude", ".agents", ".gemini", ".cursor" })
        {
            var p = Path.Combine(home, sub);
            if (Directory.Exists(p))
            {
                Directory.Delete(p, recursive: true);
            }
        }

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "skill", "install", "--target", "gemini", "--scope", "global" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        var skill = Path.Combine(home, ".gemini", "skills", "yt", "SKILL.md");
        await Assert.That(File.Exists(skill)).IsTrue();
    }

    /// <summary>
    /// <see cref="SkillManager.EnsureParent"/> идемпотентен и обрабатывает крайние случаи
    /// (пустая строка, повторные вызовы, несуществующее дерево).
    /// </summary>
    [Test]
    public async Task EnsureParent_Idempotent_HandlesEdgeCases()
    {
        using var env = new TestEnv();
        var deep = Path.Combine(env.Root, "a", "b", "c", "d", "file.md");

        SkillManager.EnsureParent(deep);
        await Assert.That(Directory.Exists(Path.GetDirectoryName(deep)!)).IsTrue();

        // Повторный вызов — no-op, не падает.
        SkillManager.EnsureParent(deep);
        await Assert.That(Directory.Exists(Path.GetDirectoryName(deep)!)).IsTrue();
    }

    /// <summary>
    /// <see cref="SkillInstallCommandHelpers.ShouldRunInteractive"/>: матрица условий.
    /// </summary>
    [Test]
    public async Task ShouldRunInteractive_Matrix()
    {
        SkillInstallCommandHelpers.TestForceInteractive.Value = true;

        // Default flow (TTY + ничего не передано) → true.
        await Assert.That(SkillInstallCommandHelpers.ShouldRunInteractive(false, false, false)).IsTrue();
        // --no-prompt → false.
        await Assert.That(SkillInstallCommandHelpers.ShouldRunInteractive(false, false, true)).IsFalse();
        // --target → false.
        await Assert.That(SkillInstallCommandHelpers.ShouldRunInteractive(true, false, false)).IsFalse();
        // --scope → false.
        await Assert.That(SkillInstallCommandHelpers.ShouldRunInteractive(false, true, false)).IsFalse();

        // Non-TTY → всегда false, даже без флагов.
        SkillInstallCommandHelpers.TestForceInteractive.Value = false;
        await Assert.That(SkillInstallCommandHelpers.ShouldRunInteractive(false, false, false)).IsFalse();

        SkillInstallCommandHelpers.TestForceInteractive.Value = null;
    }
}
