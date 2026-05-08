namespace YandexTrackerCLI.Skill;

using Spectre.Console;

/// <summary>
/// Spectre.Console-реализация <see cref="ISkillInstallPrompt"/>: использует
/// <c>MultiSelectionPrompt</c>/<c>SelectionPrompt</c>/<c>ConfirmationPrompt</c>
/// поверх <see cref="IAnsiConsole"/>, направленного в <c>stderr</c>.
/// </summary>
/// <remarks>
/// Все декорации идут в stderr — stdout остаётся bit-exact для JSON-вывода.
/// AOT-friendly: prompt-классы Spectre не используют reflection на пользовательских
/// типах (мы передаём <see cref="SkillTarget"/> и <see cref="SkillScope"/> как
/// enum-значения с явным <c>UseConverter</c>).
/// </remarks>
internal sealed class SpectreSkillInstallPrompt : ISkillInstallPrompt
{
    /// <summary>
    /// Singleton с дефолтной stderr-консолью.
    /// </summary>
    public static readonly SpectreSkillInstallPrompt Default = new(CreateDefaultConsole());

    private readonly IAnsiConsole _ansi;

    /// <summary>
    /// Создаёт prompt поверх явной <see cref="IAnsiConsole"/>. Тесты могут передавать
    /// <c>TestConsole</c>; в production используется <see cref="Default"/>.
    /// </summary>
    /// <param name="ansi">Spectre-консоль; ожидается, что её writer указывает в stderr.</param>
    public SpectreSkillInstallPrompt(IAnsiConsole ansi)
    {
        ArgumentNullException.ThrowIfNull(ansi);
        _ansi = ansi;
    }

    private static IAnsiConsole CreateDefaultConsole() =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error),
        });

    /// <inheritdoc />
    public SkillTarget[] PromptTargets(IReadOnlyList<SkillTarget> all, IReadOnlyList<SkillTarget> detected)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(detected);

        _ansi.WriteLine();
        _ansi.MarkupLine("[bold]Установка yt skill в AI-ассистенты[/]");
        _ansi.WriteLine();

        var prompt = new MultiSelectionPrompt<SkillTarget>()
            .Title("Выберите ассистентов (пробел — toggle, Enter — подтвердить):")
            .NotRequired()
            .PageSize(10)
            .MoreChoicesText("[grey](прокрутка стрелками)[/]")
            .InstructionsText("[grey]<пробел> toggle, <Enter> ok[/]")
            .UseConverter(t => LabelWithHint(t, detected));

        foreach (var t in all)
        {
            prompt.AddChoice(t);
        }
        foreach (var t in detected)
        {
            if (all.Contains(t))
            {
                prompt.Select(t);
            }
        }

        var chosen = _ansi.Prompt(prompt);
        return chosen.ToArray();
    }

    /// <inheritdoc />
    public SkillScope PromptScope()
    {
        var prompt = new SelectionPrompt<SkillScope>()
            .Title("Куда установить?")
            .AddChoices(SkillScope.Global, SkillScope.Project)
            .UseConverter(s => s switch
            {
                SkillScope.Global => "global  — для всех проектов (default)",
                SkillScope.Project => "project — только в текущий проект",
                _ => s.ToString(),
            });
        return _ansi.Prompt(prompt);
    }

    /// <inheritdoc />
    public bool PromptOverwrite(IReadOnlyList<string> existingPaths)
    {
        ArgumentNullException.ThrowIfNull(existingPaths);
        if (existingPaths.Count == 0)
        {
            return false;
        }

        _ansi.WriteLine();
        _ansi.MarkupLine("[yellow]Эти файлы уже существуют и будут перезаписаны:[/]");
        foreach (var p in existingPaths)
        {
            _ansi.MarkupLineInterpolated($"  • {p}");
        }

        var confirm = new ConfirmationPrompt($"Перезаписать {existingPaths.Count} файл(ов)?")
        {
            DefaultValue = false,
        };
        return _ansi.Prompt(confirm);
    }

    private static string LabelWithHint(SkillTarget t, IReadOnlyList<SkillTarget> detected)
    {
        var label = t switch
        {
            SkillTarget.Claude => "Claude",
            SkillTarget.Codex => "Codex",
            SkillTarget.Gemini => "Gemini",
            SkillTarget.Cursor => "Cursor",
            SkillTarget.Copilot => "Copilot",
            _ => t.ToString(),
        };
        var hint = t switch
        {
            SkillTarget.Claude => "~/.claude/skills/yt/SKILL.md",
            SkillTarget.Codex => "~/.agents/skills/yt/SKILL.md",
            SkillTarget.Gemini => "~/.gemini/skills/yt/SKILL.md",
            SkillTarget.Cursor => "~/.cursor/rules/yt.mdc",
            SkillTarget.Copilot => "только project: .github/instructions/",
            _ => string.Empty,
        };
        // Spectre интерпретирует [...] как markup-теги, поэтому используем «обнаружен» без скобок.
        var suffix = detected.Contains(t) ? " — обнаружен" : string.Empty;
        return $"{label} ({hint}){suffix}";
    }
}
