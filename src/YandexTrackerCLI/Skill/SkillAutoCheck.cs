namespace YandexTrackerCLI.Skill;

using System.Text.Json;

/// <summary>
/// Auto-check механизм: при запуске CLI проверяет, что установленный skill
/// соответствует версии бинаря; в TTY-режиме предлагает обновить, в pipe-режиме
/// один раз на версию выводит warning в stderr.
/// </summary>
/// <remarks>
/// Поведение настраивается:
/// <list type="bullet">
///   <item><description>Глобальный флаг <c>--no-skill-check</c></description></item>
///   <item><description>Env <c>YT_SKILL_CHECK=0</c> / <c>false</c></description></item>
///   <item><description>State-файл (<see cref="SkillPromptState.NeverPrompt"/> / <see cref="SkillPromptState.DeclinedForVersions"/>)</description></item>
/// </list>
/// </remarks>
public static class SkillAutoCheck
{
    /// <summary>
    /// Test-hook для подмены TTY-detection. <c>null</c> — использовать <see cref="Console.IsInputRedirected"/>/<see cref="Console.IsOutputRedirected"/>.
    /// </summary>
    public static readonly AsyncLocal<bool?> TestForceInteractive = new();

    /// <summary>
    /// Test-hook для подмены чтения ответа пользователя в interactive-режиме.
    /// Возвращает строку (без перевода строки). <c>null</c> — использовать <see cref="Console.ReadLine"/>.
    /// </summary>
    public static readonly AsyncLocal<Func<string?>?> TestPromptReader = new();

    /// <summary>
    /// Test-hook для подмены writer'ов user-facing вывода. <c>null</c> — использовать
    /// <see cref="Console.Out"/>/<see cref="Console.Error"/>. Применяется в обоих интерактивном
    /// и non-TTY-режимах для возможности юнит-тестирования.
    /// </summary>
    public static readonly AsyncLocal<TextWriter?> TestStdout = new();
    /// <summary>
    /// Test-hook для подмены stderr. См. <see cref="TestStdout"/>.
    /// </summary>
    public static readonly AsyncLocal<TextWriter?> TestStderr = new();

    /// <summary>
    /// Возвращает <c>true</c>, если auto-check должен быть пропущен исходя из аргументов команды.
    /// Пропускаем для <c>yt skill *</c>, <c>--version</c> / <c>-v</c>, <c>--help</c> / <c>-h</c>.
    /// </summary>
    /// <param name="args">Полный массив аргументов CLI.</param>
    public static bool ShouldSkipFromArgs(IReadOnlyList<string> args)
    {
        // skip if any arg is help or version request
        foreach (var a in args)
        {
            if (a is "--version" or "-v" or "--help" or "-h" or "-?" or "--no-skill-check")
            {
                return true;
            }
        }
        // skip if first non-option arg is "skill"
        foreach (var a in args)
        {
            if (string.IsNullOrEmpty(a) || a.StartsWith('-'))
            {
                continue;
            }
            return a == "skill";
        }
        return false;
    }

    /// <summary>
    /// Возвращает <c>true</c>, если env-var <c>YT_SKILL_CHECK</c> явно отключает auto-check.
    /// </summary>
    public static bool DisabledByEnv()
    {
        var v = Environment.GetEnvironmentVariable("YT_SKILL_CHECK");
        if (string.IsNullOrEmpty(v))
        {
            return false;
        }
        return v == "0" || string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Запускает проверку. Если есть устаревшие локации — в TTY рисует prompt и
    /// (при согласии) запускает <see cref="SkillManager.Update"/>. В pipe — один раз
    /// на версию выводит warning в stderr.
    /// </summary>
    /// <param name="projectDir">Корень проекта (cwd). Используется для project-scope.</param>
    public static void RunIfNeeded(string projectDir)
    {
        if (DisabledByEnv())
        {
            return;
        }

        SkillPromptState state;
        try
        {
            state = SkillPromptState.Load();
        }
        catch
        {
            // Никогда не падаем из-за state-файла.
            return;
        }

        var status = SkillManager.GetStatus(projectDir);

        // Обновляем last_seen_binary_version, но без сохранения если ничего не делаем.
        var stateChanged = false;
        if (state.LastSeenBinaryVersion != status.CurrentVersion)
        {
            state.LastSeenBinaryVersion = status.CurrentVersion;
            stateChanged = true;
        }

        if (!status.AnyOutdated)
        {
            if (stateChanged)
            {
                TrySave(state);
            }
            return;
        }

        if (state.NeverPrompt)
        {
            if (stateChanged)
            {
                TrySave(state);
            }
            return;
        }

        if (state.DeclinedForVersions.Contains(status.CurrentVersion, StringComparer.Ordinal))
        {
            if (stateChanged)
            {
                TrySave(state);
            }
            return;
        }

        if (IsInteractive())
        {
            HandleInteractive(state, status, projectDir);
        }
        else
        {
            HandleNonInteractive(state, status);
        }

        if (stateChanged)
        {
            TrySave(state);
        }
    }

    /// <summary>
    /// Test-friendly запуск: возвращает <c>true</c> если что-то изменил (для тестов).
    /// </summary>
    public static void Run(string projectDir) => RunIfNeeded(projectDir);

    private static bool IsInteractive()
    {
        if (TestForceInteractive.Value is { } forced)
        {
            return forced;
        }
        return !Console.IsInputRedirected && !Console.IsOutputRedirected;
    }

    private static TextWriter Stdout() => TestStdout.Value ?? Console.Out;
    private static TextWriter Stderr() => TestStderr.Value ?? Console.Error;

    private static void HandleInteractive(SkillPromptState state, SkillManager.Status status, string projectDir)
    {
        var outdated = status.Outdated().ToArray();
        var stdout = Stdout();

        if (outdated.Length == 1)
        {
            var i = outdated[0];
            stdout.WriteLine();
            stdout.WriteLine($"yt skill устарел: {LocLabel(i)} ({i.Path}) — {i.Version}, текущий — {status.CurrentVersion}.");
        }
        else
        {
            stdout.WriteLine();
            stdout.WriteLine("yt skill устарел в нескольких местах:");
            foreach (var i in outdated)
            {
                stdout.WriteLine($"  • {LocLabel(i)} ({i.Path}) — {i.Version}");
            }
            stdout.WriteLine($"Текущая версия — {status.CurrentVersion}.");
        }

        stdout.Write("Обновить сейчас? [Y/n/never]: ");
        stdout.Flush();

        var line = ReadPromptLine();
        var answer = (line ?? string.Empty).Trim().ToLowerInvariant();

        if (answer is "" or "y" or "yes" or "д" or "да")
        {
            var results = SkillManager.Update(
                new[]
                {
                    SkillTarget.Claude,
                    SkillTarget.Codex,
                    SkillTarget.Gemini,
                    SkillTarget.Cursor,
                    SkillTarget.Copilot,
                },
                new[] { SkillScope.Global, SkillScope.Project },
                projectDir);
            foreach (var r in results)
            {
                stdout.WriteLine($"Обновлено: {LocLabel(r.Target, r.Scope)} ({r.Path}) {r.FromVersion ?? "?"} → {r.Version}");
            }
        }
        else if (answer is "never" or "никогда")
        {
            state.NeverPrompt = true;
            stdout.WriteLine("Больше не спрашивать (yt skill check --reset-prompt-state — сбросить).");
        }
        else
        {
            // 'n' / no / прочее — отказ только для текущей версии.
            if (!state.DeclinedForVersions.Contains(status.CurrentVersion, StringComparer.Ordinal))
            {
                state.DeclinedForVersions.Add(status.CurrentVersion);
            }
            stdout.WriteLine("Пропущено для этой версии.");
        }
        TrySave(state);
    }

    private static void HandleNonInteractive(SkillPromptState state, SkillManager.Status status)
    {
        if (state.LastWarnedForVersions.Contains(status.CurrentVersion, StringComparer.Ordinal))
        {
            return;
        }

        var outdated = status.Outdated().ToArray();
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteStartObject("warning");
            w.WriteString("code", "skill_outdated");
            w.WriteString(
                "message",
                $"yt skill is outdated for current binary {status.CurrentVersion}. Run 'yt skill update' to refresh.");
            w.WriteStartArray("outdated_locations");
            foreach (var i in outdated)
            {
                w.WriteStartObject();
                w.WriteString("target", i.Target.ToString().ToLowerInvariant());
                w.WriteString("scope", i.Scope.ToString().ToLowerInvariant());
                w.WriteString("path", i.Path);
                w.WriteString("installed_version", i.Version);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
        }
        Stderr().WriteLine(System.Text.Encoding.UTF8.GetString(ms.ToArray()));
        state.LastWarnedForVersions.Add(status.CurrentVersion);
        TrySave(state);
    }

    private static string? ReadPromptLine()
    {
        if (TestPromptReader.Value is { } reader)
        {
            return reader();
        }
        return Console.ReadLine();
    }

    private static void TrySave(SkillPromptState state)
    {
        try
        {
            state.Save();
        }
        catch
        {
            // never fail user's command because of state file issues
        }
    }

    private static string LocLabel(SkillManager.Installation i) => LocLabel(i.Target, i.Scope);

    private static string LocLabel(SkillTarget t, SkillScope s) =>
        (t, s) switch
        {
            (SkillTarget.Claude, SkillScope.Global) => "Claude (global)",
            (SkillTarget.Claude, SkillScope.Project) => "Claude (project)",
            (SkillTarget.Codex, SkillScope.Global) => "Codex (global)",
            (SkillTarget.Codex, SkillScope.Project) => "Codex (project)",
            (SkillTarget.Gemini, SkillScope.Global) => "Gemini (global)",
            (SkillTarget.Gemini, SkillScope.Project) => "Gemini (project)",
            (SkillTarget.Cursor, SkillScope.Global) => "Cursor (global)",
            (SkillTarget.Cursor, SkillScope.Project) => "Cursor (project)",
            (SkillTarget.Copilot, SkillScope.Project) => "Copilot (project)",
            _ => $"{t}/{s}",
        };
}
