namespace YandexTrackerCLI.Commands;

using System.CommandLine;
using Output;

/// <summary>
/// Строит корневую команду <c>yt</c> со всеми глобальными (recursive) опциями,
/// наследуемыми subcommand'ами.
/// </summary>
public static class RootCommandBuilder
{
    /// <summary>
    /// Глобальная опция <c>--profile</c> / <c>-p</c> — имя профиля из конфига.
    /// </summary>
    public static readonly Option<string?> ProfileOption =
        new("--profile", "-p")
        {
            Description = "Имя профиля из конфига.",
            Recursive = true,
        };

    /// <summary>
    /// Глобальная опция <c>--read-only</c> — принудительно включить read-only режим.
    /// </summary>
    public static readonly Option<bool> ReadOnlyOption =
        new("--read-only")
        {
            Description = "Принудительно включить read-only режим (mutating HTTP запрещены).",
            Recursive = true,
        };

    /// <summary>
    /// Глобальная опция <c>--format</c> — формат вывода: <c>auto</c> (default) | <c>json</c> |
    /// <c>minimal</c> | <c>table</c>. Значение <c>auto</c> резолвится в <c>table</c> для TTY и
    /// <c>json</c> для перенаправленного stdout (с учётом env <c>YT_FORMAT</c> и
    /// <c>profile.default_format</c>).
    /// </summary>
    public static readonly Option<OutputFormat> FormatOption =
        new("--format")
        {
            Description = "auto (default, TTY→table, pipe→json) | json | minimal | table.",
            Recursive = true,
            DefaultValueFactory = _ => OutputFormat.Auto,
        };

    /// <summary>
    /// Глобальная опция <c>--timeout</c> — HTTP timeout в секундах (default 30).
    /// </summary>
    public static readonly Option<int?> TimeoutOption =
        new("--timeout")
        {
            Description = "HTTP timeout в секундах (default 30).",
            Recursive = true,
        };

    /// <summary>
    /// Глобальная опция <c>--no-color</c> — отключить ANSI цвета.
    /// </summary>
    public static readonly Option<bool> NoColorOption =
        new("--no-color")
        {
            Description = "Отключить ANSI цвета.",
            Recursive = true,
        };

    /// <summary>
    /// Глобальная опция <c>--no-pager</c> — отключить авто-pager в detail/comment view.
    /// Альтернатива env <c>YT_PAGER=cat</c> или <c>YT_PAGER=</c> (пустая).
    /// </summary>
    public static readonly Option<bool> NoPagerOption =
        new("--no-pager")
        {
            Description = "Отключить pager (less) для detail view и list of comments.",
            Recursive = true,
        };

    /// <summary>
    /// Глобальная опция <c>--log-file</c> — путь к файлу wire-log (запись запросов и
    /// ответов HTTP с маскированием секретов; альтернатива env <c>YT_LOG_FILE</c>).
    /// </summary>
    public static readonly Option<string?> LogFileOption =
        new("--log-file")
        {
            Description = "Путь к файлу wire-log (HTTP request/response трейс с маскированием секретов).",
            Recursive = true,
        };

    /// <summary>
    /// Глобальная опция <c>--log-raw</c> — отключить маскирование секретов в wire-log.
    /// Альтернатива env <c>YT_LOG_RAW=1</c>. Используется ТОЛЬКО для отладки (файл
    /// будет содержать живые токены, секреты и authorization codes — удалить после).
    /// </summary>
    public static readonly Option<bool> LogRawOption =
        new("--log-raw")
        {
            Description = "Отключить маскирование секретов в wire-log. Только для debug — файл будет содержать живые токены и authorization codes.",
            Recursive = true,
        };

    /// <summary>
    /// Глобальная опция <c>--no-skill-check</c> — отключить авто-проверку устаревшего
    /// AI-skill (Claude/Codex) для одного вызова. Альтернатива env <c>YT_SKILL_CHECK=0</c>.
    /// </summary>
    public static readonly Option<bool> NoSkillCheckOption =
        new("--no-skill-check")
        {
            Description = "Отключить авто-проверку устаревшего AI-skill (Claude/Codex).",
            Recursive = true,
        };

    /// <summary>
    /// Собирает корневую команду с зарегистрированными глобальными опциями.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="RootCommand"/>.</returns>
    public static RootCommand Build()
    {
        var root = new RootCommand("yt — Yandex Tracker CLI");
        root.Options.Add(ProfileOption);
        root.Options.Add(ReadOnlyOption);
        root.Options.Add(FormatOption);
        root.Options.Add(TimeoutOption);
        root.Options.Add(NoColorOption);
        root.Options.Add(NoPagerOption);
        root.Options.Add(LogFileOption);
        root.Options.Add(LogRawOption);
        root.Options.Add(NoSkillCheckOption);

        var auth = new Command("auth", "Управление аутентификацией.");
        auth.Subcommands.Add(Auth.AuthStatusCommand.Build());
        auth.Subcommands.Add(Auth.AuthLoginCommand.Build());
        auth.Subcommands.Add(Auth.AuthLogoutCommand.Build());
        root.Subcommands.Add(auth);

        var config = new Command("config", "Управление локальной конфигурацией.");
        config.Subcommands.Add(Config.ConfigListCommand.Build());
        config.Subcommands.Add(Config.ConfigGetCommand.Build());
        config.Subcommands.Add(Config.ConfigSetCommand.Build());
        config.Subcommands.Add(Config.ConfigProfileCommand.Build());
        root.Subcommands.Add(config);

        root.Subcommands.Add(User.UserCommandBuilder.Build());

        var queue = new Command("queue", "Очереди.");
        queue.Subcommands.Add(Queue.QueueListCommand.Build());
        root.Subcommands.Add(queue);

        root.Subcommands.Add(Issue.IssueCommandBuilder.Build());
        root.Subcommands.Add(Automation.AutomationCommandBuilder.Build());
        root.Subcommands.Add(Comment.CommentCommandBuilder.Build());
        root.Subcommands.Add(Worklog.WorklogCommandBuilder.Build());
        root.Subcommands.Add(Attachment.AttachmentCommandBuilder.Build());
        root.Subcommands.Add(Checklist.ChecklistCommandBuilder.Build());
        root.Subcommands.Add(Link.LinkCommandBuilder.Build());
        root.Subcommands.Add(Board.BoardCommandBuilder.Build());
        root.Subcommands.Add(Sprint.SprintCommandBuilder.Build());
        root.Subcommands.Add(Project.ProjectCommandBuilder.Build());
        root.Subcommands.Add(Component.ComponentCommandBuilder.Build());
        root.Subcommands.Add(Version.VersionCommandBuilder.Build());
        root.Subcommands.Add(Field.FieldCommandBuilder.Build());
        root.Subcommands.Add(Ref.RefCommandBuilder.Build());
        root.Subcommands.Add(Skill.SkillCommandBuilder.Build());

        return root;
    }
}
