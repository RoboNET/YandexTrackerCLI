namespace YandexTrackerCLI.Tests.Commands.Skill;

using Spectre.Console;
using Spectre.Console.Testing;
using TUnit.Core;
using YandexTrackerCLI.Skill;

/// <summary>
/// Тесты <see cref="SpectreSkillInstallPrompt"/> поверх <see cref="TestConsole"/>.
/// Покрывают базовые сценарии prompt'ов; некоторые multi-select сценарии требуют
/// эмуляции стрелок и Space — в TUnit + Spectre TestConsole это поддерживается через
/// <c>Input.PushKey</c>.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class SpectreSkillInstallPromptTests
{
    private static TestConsole CreateTestConsole()
    {
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        return console;
    }

    [Test]
    public async Task PromptScope_DefaultGlobal_OnEnter()
    {
        var console = CreateTestConsole();
        // Первое значение в SelectionPrompt — Global (default).
        console.Input.PushKey(ConsoleKey.Enter);

        var prompt = new SpectreSkillInstallPrompt(console);
        var scope = prompt.PromptScope();

        await Assert.That(scope).IsEqualTo(SkillScope.Global);
    }

    [Test]
    public async Task PromptScope_DownEnter_PicksProject()
    {
        var console = CreateTestConsole();
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);

        var prompt = new SpectreSkillInstallPrompt(console);
        var scope = prompt.PromptScope();

        await Assert.That(scope).IsEqualTo(SkillScope.Project);
    }

    [Test]
    public async Task PromptOverwrite_EmptyPaths_ReturnsFalseWithoutPrompt()
    {
        var console = CreateTestConsole();
        var prompt = new SpectreSkillInstallPrompt(console);
        var result = prompt.PromptOverwrite(Array.Empty<string>());
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task PromptOverwrite_DefaultFalse_OnEnter()
    {
        var console = CreateTestConsole();
        // ConfirmationPrompt: Enter → DefaultValue (false).
        console.Input.PushKey(ConsoleKey.Enter);

        var prompt = new SpectreSkillInstallPrompt(console);
        var result = prompt.PromptOverwrite(new[] { "/tmp/x", "/tmp/y" });
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task PromptOverwrite_YInput_ReturnsTrue()
    {
        var console = CreateTestConsole();
        console.Input.PushTextWithEnter("y");

        var prompt = new SpectreSkillInstallPrompt(console);
        var result = prompt.PromptOverwrite(new[] { "/tmp/x" });
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task PromptTargets_NoSelection_AcceptsPreselectedDetected()
    {
        var console = CreateTestConsole();
        // MultiSelectionPrompt: Enter без toggle — берём pre-selected (detected).
        console.Input.PushKey(ConsoleKey.Enter);

        var prompt = new SpectreSkillInstallPrompt(console);
        var all = new[]
        {
            SkillTarget.Claude, SkillTarget.Codex, SkillTarget.Gemini,
            SkillTarget.Cursor, SkillTarget.Copilot,
        };
        var detected = new[] { SkillTarget.Claude, SkillTarget.Gemini };

        var chosen = prompt.PromptTargets(all, detected);

        await Assert.That(chosen.Length).IsEqualTo(2);
        await Assert.That(chosen).Contains(SkillTarget.Claude);
        await Assert.That(chosen).Contains(SkillTarget.Gemini);
    }

    [Test]
    public async Task PromptTargets_NotRequired_EmptyDetected_AllowsZeroSelection()
    {
        var console = CreateTestConsole();
        console.Input.PushKey(ConsoleKey.Enter);

        var prompt = new SpectreSkillInstallPrompt(console);
        var all = new[] { SkillTarget.Claude, SkillTarget.Codex };
        var detected = Array.Empty<SkillTarget>();

        var chosen = prompt.PromptTargets(all, detected);
        // NotRequired() позволяет вернуть пустой массив.
        await Assert.That(chosen.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Default_SingletonExists()
    {
        // Smoke-test: Default не падает при создании (stderr-консоль).
        await Assert.That(SpectreSkillInstallPrompt.Default).IsNotNull();
    }
}
