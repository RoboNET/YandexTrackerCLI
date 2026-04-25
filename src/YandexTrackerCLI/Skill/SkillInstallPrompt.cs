namespace YandexTrackerCLI.Skill;

using System.Globalization;
using YandexTrackerCLI.Core;

/// <summary>
/// Интерактивный prompt для команды <c>yt skill install</c>: выбор target'ов, scope
/// и подтверждение перезаписи. Реализован в стиле минимального TTY-prompt'а
/// (без Spectre.Console, AOT-friendly), по аналогии с <see cref="SkillAutoCheck"/>.
/// </summary>
/// <remarks>
/// Поведение:
/// <list type="bullet">
///   <item><description>Активируется в <see cref="SkillInstallCommandHelpers.IsInteractive"/>
///         (TTY + явные flag'и не переданы + <c>--no-prompt</c> отсутствует).</description></item>
///   <item><description>По дефолту предлагает выбрать ассистентов, у которых есть базовая
///         директория (<c>~/.claude</c>, <c>~/.gemini</c>, ...).</description></item>
///   <item><description>В тестах подменяется через <see cref="TestOverride"/> — AsyncLocal
///         с фейковой реализацией, по тому же паттерну, что и <c>TestTokenReader</c>.</description></item>
/// </list>
/// </remarks>
public static class SkillInstallPrompt
{
    /// <summary>
    /// Test-override: AsyncLocal-фейк, заменяющий чтение пользовательского ввода
    /// и/или весь интерактивный flow. <c>null</c> — использовать stdin/stderr.
    /// </summary>
    public static readonly AsyncLocal<ISkillInstallPrompt?> TestOverride = new();

    /// <summary>
    /// Возвращает фактическую реализацию prompt'а: либо <see cref="TestOverride"/>, либо
    /// console-based default.
    /// </summary>
    public static ISkillInstallPrompt Current => TestOverride.Value ?? ConsoleSkillInstallPrompt.Instance;
}

/// <summary>
/// Абстракция интерактивного prompt'а для <c>yt skill install</c>. Позволяет тестам
/// предсказуемо контролировать выбор target/scope/force без реального TTY.
/// </summary>
public interface ISkillInstallPrompt
{
    /// <summary>
    /// Просит пользователя выбрать ассистентов для установки.
    /// </summary>
    /// <param name="all">Все доступные target'ы.</param>
    /// <param name="detected">Подмножество <paramref name="all"/>, у которых обнаружен
    /// базовый каталог (<c>~/.claude</c>, <c>~/.gemini</c>, ...) — будет offered как default.</param>
    /// <returns>Выбранные target'ы (могут быть пустым массивом — пользователь отказался).</returns>
    SkillTarget[] PromptTargets(IReadOnlyList<SkillTarget> all, IReadOnlyList<SkillTarget> detected);

    /// <summary>
    /// Просит пользователя выбрать scope (global / project).
    /// </summary>
    /// <returns>Выбранный scope (default — <see cref="SkillScope.Global"/>).</returns>
    SkillScope PromptScope();

    /// <summary>
    /// Просит подтвердить перезапись существующих файлов.
    /// </summary>
    /// <param name="existingPaths">Список путей, которые уже существуют и будут перезаписаны.</param>
    /// <returns><c>true</c> — да, перезаписывать (соответствует <c>--force</c>).</returns>
    bool PromptOverwrite(IReadOnlyList<string> existingPaths);
}

/// <summary>
/// Console-based реализация <see cref="ISkillInstallPrompt"/>: пишет prompt в stderr
/// (чтобы не загрязнять stdout-JSON команды), читает <see cref="Console.ReadLine"/>.
/// Сообщения локализованы на русском (как и остальные user-facing строки в проекте).
/// </summary>
internal sealed class ConsoleSkillInstallPrompt : ISkillInstallPrompt
{
    public static readonly ConsoleSkillInstallPrompt Instance = new();

    /// <inheritdoc />
    public SkillTarget[] PromptTargets(IReadOnlyList<SkillTarget> all, IReadOnlyList<SkillTarget> detected)
    {
        var err = Console.Error;
        err.WriteLine();
        err.WriteLine("Установка yt skill в AI-ассистенты");
        err.WriteLine("══════════════════════════════════");
        err.WriteLine();
        err.WriteLine("Доступные ассистенты:");
        for (var i = 0; i < all.Count; i++)
        {
            var t = all[i];
            var mark = detected.Contains(t) ? "✓" : " ";
            err.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  [{mark}] {i + 1}) {LabelFor(t),-8} ({HintFor(t)})"));
        }
        err.WriteLine();
        err.WriteLine("Введите номера через запятую, 'a' = все, '0' = ничего.");
        if (detected.Count > 0)
        {
            err.WriteLine($"Default (Enter): обнаруженные [{string.Join(", ", detected.Select(d => LabelFor(d).ToLowerInvariant()))}].");
        }
        else
        {
            err.WriteLine("Default (Enter): все.");
        }
        err.Write("Выбор: ");
        err.Flush();
        var line = Console.ReadLine();
        return ParseTargetsInput(line, all, detected);
    }

    /// <inheritdoc />
    public SkillScope PromptScope()
    {
        var err = Console.Error;
        err.WriteLine();
        err.WriteLine("Куда установить?");
        err.WriteLine("  1) global  — для всех проектов (default)");
        err.WriteLine("  2) project — только в текущий проект");
        err.Write("Выбор [1]: ");
        err.Flush();
        var line = Console.ReadLine();
        return ParseScopeInput(line);
    }

    /// <inheritdoc />
    public bool PromptOverwrite(IReadOnlyList<string> existingPaths)
    {
        if (existingPaths.Count == 0)
        {
            return false;
        }
        var err = Console.Error;
        err.WriteLine();
        err.WriteLine("Эти файлы уже существуют и будут перезаписаны:");
        foreach (var p in existingPaths)
        {
            err.WriteLine($"  • {p}");
        }
        err.Write("Перезаписать? [y/N]: ");
        err.Flush();
        var line = Console.ReadLine();
        var ans = (line ?? string.Empty).Trim().ToLowerInvariant();
        return ans is "y" or "yes" or "д" or "да";
    }

    /// <summary>
    /// Парсит ввод target-списка. Пустой ввод → detected (или all, если detected пуст).
    /// </summary>
    /// <param name="raw">Сырой ввод пользователя.</param>
    /// <param name="all">Все доступные target'ы (1-based индексы).</param>
    /// <param name="detected">Auto-detected target'ы — default при пустом вводе.</param>
    /// <returns>Выбранные target'ы; пустой массив при <c>0</c>.</returns>
    public static SkillTarget[] ParseTargetsInput(
        string? raw, IReadOnlyList<SkillTarget> all, IReadOnlyList<SkillTarget> detected)
    {
        var input = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(input))
        {
            return detected.Count > 0 ? detected.ToArray() : all.ToArray();
        }
        if (input is "a" or "all" or "*")
        {
            return all.ToArray();
        }
        if (input is "0" or "n" or "none" or "нет")
        {
            return Array.Empty<SkillTarget>();
        }

        var result = new List<SkillTarget>();
        foreach (var part in input.Split(',', ' ', ';'))
        {
            var token = part.Trim();
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }
            // Сначала пробуем как номер (1-based).
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                && n >= 1 && n <= all.Count)
            {
                var t = all[n - 1];
                if (!result.Contains(t))
                {
                    result.Add(t);
                }
                continue;
            }
            // Иначе — по имени.
            var byName = TryParseTargetName(token);
            if (byName is { } resolved && !result.Contains(resolved))
            {
                result.Add(resolved);
            }
        }
        // Если ничего не распарсили — fallback на detected/all (как при пустом вводе).
        if (result.Count == 0)
        {
            return detected.Count > 0 ? detected.ToArray() : all.ToArray();
        }
        return result.ToArray();
    }

    /// <summary>
    /// Парсит ввод scope. Пустой ввод / "1" / "global" → <see cref="SkillScope.Global"/>.
    /// "2" / "project" → <see cref="SkillScope.Project"/>.
    /// </summary>
    public static SkillScope ParseScopeInput(string? raw)
    {
        var input = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return input switch
        {
            "" or "1" or "global" or "g" or "глобально" => SkillScope.Global,
            "2" or "project" or "p" or "проект" => SkillScope.Project,
            _ => SkillScope.Global,
        };
    }

    private static SkillTarget? TryParseTargetName(string name) => name switch
    {
        "claude" => SkillTarget.Claude,
        "codex" => SkillTarget.Codex,
        "gemini" => SkillTarget.Gemini,
        "cursor" => SkillTarget.Cursor,
        "copilot" => SkillTarget.Copilot,
        _ => null,
    };

    private static string LabelFor(SkillTarget t) => t switch
    {
        SkillTarget.Claude => "Claude",
        SkillTarget.Codex => "Codex",
        SkillTarget.Gemini => "Gemini",
        SkillTarget.Cursor => "Cursor",
        SkillTarget.Copilot => "Copilot",
        _ => t.ToString(),
    };

    private static string HintFor(SkillTarget t) => t switch
    {
        SkillTarget.Claude => "~/.claude/skills/yt/SKILL.md",
        SkillTarget.Codex => "~/.agents/skills/yt/SKILL.md",
        SkillTarget.Gemini => "~/.gemini/skills/yt/SKILL.md",
        SkillTarget.Cursor => "~/.cursor/rules/yt.mdc",
        SkillTarget.Copilot => "только project: .github/instructions/",
        _ => string.Empty,
    };
}

/// <summary>
/// Auto-detect: ищет в HOME-каталоге характерные подкаталоги установленных AI-CLI
/// (<c>~/.claude</c>, <c>~/.codex</c>/<c>.agents</c>, <c>~/.gemini</c>, <c>~/.cursor</c>).
/// Используется как default-предложение в интерактивном prompt'е.
/// </summary>
public static class SkillInstallDetector
{
    /// <summary>
    /// Возвращает список target'ов, у которых обнаружен соответствующий базовый каталог
    /// в HOME пользователя. Copilot не имеет global-каталога, поэтому всегда фильтруется
    /// (если <paramref name="includeCopilot"/> = <c>false</c>).
    /// </summary>
    /// <param name="includeCopilot">Включать ли Copilot в результат (имеет смысл для project-scope:
    /// тогда мы добавляем его, если <c>.github</c>-каталог есть в текущем cwd).</param>
    /// <param name="projectDir">Корень проекта; используется для project-scope detection (Copilot/Cursor/etc).</param>
    /// <returns>Подмножество <see cref="SkillTarget"/>, упорядоченное как в <see cref="SkillInstallPrompt"/>.</returns>
    public static IReadOnlyList<SkillTarget> DetectInstalled(bool includeCopilot, string? projectDir = null)
    {
        var home = PathResolver.ResolveHome();
        var found = new List<SkillTarget>();

        if (!string.IsNullOrEmpty(home))
        {
            if (Directory.Exists(Path.Combine(home, ".claude"))) found.Add(SkillTarget.Claude);
            // Codex: проверяем оба варианта на всякий случай — старый ~/.codex и текущий ~/.agents.
            if (Directory.Exists(Path.Combine(home, ".agents")) || Directory.Exists(Path.Combine(home, ".codex")))
            {
                found.Add(SkillTarget.Codex);
            }
            if (Directory.Exists(Path.Combine(home, ".gemini"))) found.Add(SkillTarget.Gemini);
            if (Directory.Exists(Path.Combine(home, ".cursor"))) found.Add(SkillTarget.Cursor);
        }

        if (includeCopilot && !string.IsNullOrEmpty(projectDir)
            && Directory.Exists(Path.Combine(projectDir, ".github")))
        {
            found.Add(SkillTarget.Copilot);
        }

        return found;
    }
}
