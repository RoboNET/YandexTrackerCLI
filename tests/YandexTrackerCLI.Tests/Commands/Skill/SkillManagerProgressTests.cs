namespace YandexTrackerCLI.Tests.Commands.Skill;

using TUnit.Core;
using YandexTrackerCLI.Skill;

/// <summary>
/// Тесты overload'а <see cref="SkillManager.Update(System.Collections.Generic.IReadOnlyCollection{SkillTarget}, System.Collections.Generic.IReadOnlyCollection{SkillScope}, string, System.IProgress{SkillProgressEvent}?)"/>
/// с progress-callback'ом.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class SkillManagerProgressTests
{
    private static void InstallClaude(TestEnv env)
    {
        var path = SkillPaths.Resolve(SkillTarget.Claude, SkillScope.Global, env.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, EmbeddedSkill.ReadAll());
    }

    [Test]
    public async Task Update_WithProgress_EmitsStartedAndWroteForEachInstalled()
    {
        using var env = new TestEnv();
        InstallClaude(env);

        var events = new List<SkillProgressEvent>();

        // Synchronous IProgress<T>, чтобы избежать гонки с <see cref="Progress{T}"/>,
        // который диспатчит callbacks через SyncContext или thread pool — события могут
        // прийти после await Task.Yield() в TUnit.
        var progress = new SyncProgress(events.Add);

        var results = SkillManager.Update(
            new[] { SkillTarget.Claude, SkillTarget.Codex },
            new[] { SkillScope.Global, SkillScope.Project },
            env.Root,
            progress);
        await Task.Yield();

        await Assert.That(results.Count).IsEqualTo(1);

        var started = events.Where(e => e.Kind == SkillProgressKind.Started).ToArray();
        var wrote = events.Where(e => e.Kind == SkillProgressKind.Wrote).ToArray();

        await Assert.That(started.Length).IsEqualTo(1);
        await Assert.That(wrote.Length).IsEqualTo(1);
        await Assert.That(started[0].Target).IsEqualTo(SkillTarget.Claude);
        await Assert.That(started[0].Scope).IsEqualTo(SkillScope.Global);
        await Assert.That(wrote[0].Version).IsEqualTo(EmbeddedSkill.GetVersion());
    }

    [Test]
    public async Task Update_NullProgress_DoesNotThrow()
    {
        using var env = new TestEnv();
        InstallClaude(env);

        var results = SkillManager.Update(
            new[] { SkillTarget.Claude },
            new[] { SkillScope.Global },
            env.Root,
            progress: null);

        await Assert.That(results.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Update_LegacyOverload_StillWorks()
    {
        using var env = new TestEnv();
        InstallClaude(env);

        // Старый overload без progress.
        var results = SkillManager.Update(
            new[] { SkillTarget.Claude },
            new[] { SkillScope.Global },
            env.Root);

        await Assert.That(results.Count).IsEqualTo(1);
        await Assert.That(results[0].Target).IsEqualTo(SkillTarget.Claude);
    }

    [Test]
    public async Task Update_NoInstalledLocations_EmitsNoEvents()
    {
        using var env = new TestEnv();
        // Ничего не ставим.

        var events = new List<SkillProgressEvent>();
        var progress = new Progress<SkillProgressEvent>(events.Add);

        var results = SkillManager.Update(
            new[] { SkillTarget.Claude },
            new[] { SkillScope.Global },
            env.Root,
            progress);

        await Task.Yield();

        await Assert.That(results.Count).IsEqualTo(0);
        await Assert.That(events.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Update_StartedEmittedBeforeWrote()
    {
        using var env = new TestEnv();
        InstallClaude(env);

        var events = new List<SkillProgressEvent>();
        // Используем синхронный IProgress (lambda Action) чтобы порядок был детерминирован.
        var sync = new SyncProgress(events.Add);

        SkillManager.Update(
            new[] { SkillTarget.Claude },
            new[] { SkillScope.Global },
            env.Root,
            sync);

        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(events[0].Kind).IsEqualTo(SkillProgressKind.Started);
        await Assert.That(events[1].Kind).IsEqualTo(SkillProgressKind.Wrote);
    }

    private sealed class SyncProgress : IProgress<SkillProgressEvent>
    {
        private readonly Action<SkillProgressEvent> _h;
        public SyncProgress(Action<SkillProgressEvent> h) => _h = h;
        public void Report(SkillProgressEvent value) => _h(value);
    }
}
