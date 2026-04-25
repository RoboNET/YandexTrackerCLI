namespace YandexTrackerCLI.Output;

using System.Globalization;

/// <summary>
/// Snapshot возможностей текущего терминала: цвета, OSC 8 hyperlinks, ширина,
/// pager. Резолвится из переменных окружения и явных CLI-флагов один раз
/// при создании <see cref="TrackerContext"/> и далее передаётся в рендереры.
/// </summary>
/// <param name="IsOutputRedirected">stdout перенаправлен (pipe/file) — disables все TTY-фичи.</param>
/// <param name="UseColor">Разрешено выводить ANSI-цвета и атрибуты.</param>
/// <param name="UseHyperlinks">Разрешено эмитить OSC 8 ссылки (<c>\e]8;;URL\e\\TEXT\e]8;;\e\\</c>).</param>
/// <param name="Width">Эффективная ширина терминала в колонках (clamp [40, 200]).</param>
/// <param name="UsePager">Включён ли pager-wrapper для команд detail/comment view.</param>
/// <param name="PagerCommand">Полная shell-команда для запуска pager (например <c>less -R -F -X</c>).</param>
public sealed record TerminalCapabilities(
    bool IsOutputRedirected,
    bool UseColor,
    bool UseHyperlinks,
    int Width,
    bool UsePager,
    string PagerCommand)
{
    /// <summary>
    /// Минимальная допустимая ширина терминала.
    /// </summary>
    public const int MinWidth = 40;

    /// <summary>
    /// Максимальная допустимая ширина терминала.
    /// </summary>
    public const int MaxWidth = 200;

    /// <summary>
    /// Ширина по умолчанию, когда ни env <c>YT_TERMINAL_WIDTH</c>, ни <see cref="Console.WindowWidth"/>
    /// не дают значимого результата.
    /// </summary>
    public const int DefaultWidth = 100;

    /// <summary>
    /// Pager по умолчанию, если ни <c>YT_PAGER</c>, ни <c>PAGER</c> не заданы.
    /// </summary>
    public const string DefaultPager = "less -R -F -X";

    /// <summary>
    /// Возвращает «no-op»-конфигурацию (всё выключено, ширина 100). Используется как
    /// безопасное значение по умолчанию в тестах и в случаях, когда <see cref="TrackerContext"/>
    /// ещё не построен.
    /// </summary>
    public static TerminalCapabilities Disabled { get; } = new(
        IsOutputRedirected: true,
        UseColor: false,
        UseHyperlinks: false,
        Width: DefaultWidth,
        UsePager: false,
        PagerCommand: DefaultPager);

    /// <summary>
    /// Резолвит набор возможностей терминала с учётом env, CLI-флагов и состояния stdout.
    /// </summary>
    /// <param name="env">Snapshot переменных окружения. Используются ключи: <c>NO_COLOR</c>,
    /// <c>TERM</c>, <c>TERM_PROGRAM</c>, <c>COLORTERM</c>, <c>YT_HYPERLINKS</c>,
    /// <c>YT_TERMINAL_WIDTH</c>, <c>YT_PAGER</c>, <c>PAGER</c>.</param>
    /// <param name="noColorFlag">CLI-флаг <c>--no-color</c>.</param>
    /// <param name="noPagerFlag">CLI-флаг <c>--no-pager</c>.</param>
    /// <param name="isOutputRedirected">Делегат для проверки <see cref="Console.IsOutputRedirected"/>
    /// (тестируемо).</param>
    /// <param name="consoleWidth">Делегат для получения <see cref="Console.WindowWidth"/>;
    /// возвращает <c>null</c>, если значение недоступно (non-TTY).</param>
    /// <returns>Резолвленный <see cref="TerminalCapabilities"/>.</returns>
    public static TerminalCapabilities Detect(
        IReadOnlyDictionary<string, string?> env,
        bool noColorFlag,
        bool noPagerFlag,
        Func<bool> isOutputRedirected,
        Func<int?> consoleWidth)
    {
        var redirected = isOutputRedirected();

        var noColorEnv = !string.IsNullOrEmpty(GetEnv(env, "NO_COLOR"));
        var termIsDumb = string.Equals(GetEnv(env, "TERM"), "dumb", StringComparison.OrdinalIgnoreCase);

        var useColor = !redirected && !noColorFlag && !noColorEnv && !termIsDumb;

        var useHyperlinks = ResolveHyperlinks(env, redirected, termIsDumb);
        var width = ResolveWidth(env, consoleWidth);
        var (usePager, pagerCommand) = ResolvePager(env, redirected, noPagerFlag);

        return new TerminalCapabilities(
            IsOutputRedirected: redirected,
            UseColor: useColor,
            UseHyperlinks: useHyperlinks,
            Width: width,
            UsePager: usePager,
            PagerCommand: pagerCommand);
    }

    private static bool ResolveHyperlinks(
        IReadOnlyDictionary<string, string?> env,
        bool redirected,
        bool termIsDumb)
    {
        if (redirected || termIsDumb)
        {
            return false;
        }

        // Force on/off через env override.
        var ytHyper = GetEnv(env, "YT_HYPERLINKS");
        if (!string.IsNullOrEmpty(ytHyper))
        {
            return IsTruthy(ytHyper);
        }

        // Эвристики «современный терминал».
        var termProgram = GetEnv(env, "TERM_PROGRAM") ?? string.Empty;
        if (IsKnownModernTermProgram(termProgram))
        {
            return true;
        }

        var colorTerm = GetEnv(env, "COLORTERM") ?? string.Empty;
        if (string.Equals(colorTerm, "truecolor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(colorTerm, "24bit", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var term = GetEnv(env, "TERM") ?? string.Empty;
        if (term.StartsWith("xterm-", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsKnownModernTermProgram(string termProgram) =>
        termProgram switch
        {
            "iTerm.app"       => true,
            "vscode"          => true,
            "WezTerm"         => true,
            "ghostty"         => true,
            "kitty"           => true,
            "Apple_Terminal"  => true,
            _                 => false,
        };

    private static int ResolveWidth(
        IReadOnlyDictionary<string, string?> env,
        Func<int?> consoleWidth)
    {
        var explicitWidth = GetEnv(env, "YT_TERMINAL_WIDTH");
        if (!string.IsNullOrEmpty(explicitWidth)
            && int.TryParse(explicitWidth, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromEnv)
            && fromEnv > 0)
        {
            return Clamp(fromEnv);
        }

        var fromConsole = consoleWidth();
        if (fromConsole is { } cw && cw > 0)
        {
            return Clamp(cw);
        }

        return DefaultWidth;
    }

    private static (bool UsePager, string Command) ResolvePager(
        IReadOnlyDictionary<string, string?> env,
        bool redirected,
        bool noPagerFlag)
    {
        // Note: для YT_PAGER важно отличить «не задан» (null/key absent) от
        // «задан и пуст» (явный override "выключи pager"). Поэтому здесь работаем
        // напрямую с env.TryGetValue.
        var ytPagerRaw = env.TryGetValue("YT_PAGER", out var ytv) ? ytv : null;
        var ytPager = string.IsNullOrEmpty(ytPagerRaw) ? null : ytPagerRaw;
        var sysPager = GetEnv(env, "PAGER");

        // Команда pager (для consumer-кода) — определяется независимо от того,
        // включён ли pager: rendering/тесты могут хотеть знать «what would we run».
        string command;
        if (!string.IsNullOrEmpty(ytPager))
        {
            command = ytPager;
        }
        else if (!string.IsNullOrEmpty(sysPager))
        {
            command = sysPager;
        }
        else
        {
            command = DefaultPager;
        }

        if (redirected || noPagerFlag)
        {
            return (false, command);
        }

        // YT_PAGER="" (явно пустая) или "cat" — выключить pager.
        if (ytPagerRaw is not null
            && (ytPagerRaw.Length == 0 || string.Equals(ytPagerRaw.Trim(), "cat", StringComparison.OrdinalIgnoreCase)))
        {
            return (false, command);
        }

        return (true, command);
    }

    private static int Clamp(int value)
    {
        if (value < MinWidth)
        {
            return MinWidth;
        }
        if (value > MaxWidth)
        {
            return MaxWidth;
        }
        return value;
    }

    private static string? GetEnv(IReadOnlyDictionary<string, string?> env, string key) =>
        env.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : null;

    private static bool IsTruthy(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        var v = value.Trim();
        if (string.Equals(v, "0", StringComparison.Ordinal)
            || string.Equals(v, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return true;
    }
}
