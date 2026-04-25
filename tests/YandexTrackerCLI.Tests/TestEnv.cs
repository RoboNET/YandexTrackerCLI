namespace YandexTrackerCLI.Tests;

using System.CommandLine;
using YandexTrackerCLI.Commands;

/// <summary>
/// Изолированное окружение для end-to-end тестов команд:
/// временный <c>YT_CONFIG_PATH</c> + stdin/stdout/stderr перехвачены <see cref="StringWriter"/>.
/// </summary>
internal sealed class TestEnv : IDisposable
{
    /// <summary>
    /// Минимальный валидный JSON-конфиг с одним OAuth-профилем — удобный shortcut
    /// для тестов, которым достаточно любой аутентификации, чтобы дойти до HTTP-уровня.
    /// </summary>
    public const string MinimalOAuthConfig =
        """{"default_profile":"default","profiles":{"default":{"org_type":"cloud","org_id":"o","read_only":false,"auth":{"type":"oauth","token":"y0_X"}}}}""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "yt-cli-env-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> _backup = new();

    /// <summary>
    /// Путь к временному конфигу; также прописан в <c>YT_CONFIG_PATH</c>.
    /// </summary>
    public string ConfigPath => Path.Combine(_dir, "config.json");

    /// <summary>
    /// Корень временной директории, используется как fake-HOME и project-dir в тестах.
    /// </summary>
    public string Root => _dir;

    /// <summary>
    /// Инициализирует временную директорию и указывает на неё <c>YT_CONFIG_PATH</c>.
    /// </summary>
    public TestEnv()
    {
        Directory.CreateDirectory(_dir);
        // Гарантируем, что никакие внешние переменные не повлияют на тест.
        Set("YT_CONFIG_PATH", ConfigPath);
        Set("YT_PROFILE", null);
        Set("YT_OAUTH_TOKEN", null);
        Set("YT_IAM_TOKEN", null);
        Set("YT_SERVICE_ACCOUNT_ID", null);
        Set("YT_SERVICE_ACCOUNT_KEY_ID", null);
        Set("YT_SERVICE_ACCOUNT_KEY_FILE", null);
        Set("YT_SERVICE_ACCOUNT_KEY_PEM", null);
        Set("YT_ORG_TYPE", null);
        Set("YT_ORG_ID", null);
        Set("YT_READ_ONLY", null);
        Set("YT_FORMAT", null);
        // Sandbox XDG paths so federated DPoP keys and IAM token caches don't leak
        // onto the developer's real filesystem during tests.
        Set("XDG_CONFIG_HOME", Path.Combine(_dir, "xdg-config"));
        Set("XDG_CACHE_HOME", Path.Combine(_dir, "xdg-cache"));
        // HOME / USERPROFILE — для тестов skill-команд (Claude global = ~/.claude/...,
        // Codex global = ~/.codex/...). На .NET Environment.SpecialFolder.UserProfile
        // на Unix читает HOME, на Windows — USERPROFILE.
        Set("HOME", Path.Combine(_dir, "home"));
        Set("USERPROFILE", Path.Combine(_dir, "home"));
        Directory.CreateDirectory(Path.Combine(_dir, "home"));
        // Disable skill auto-check by default — тесты должны быть детерминистичными,
        // не должны спрашивать про обновление skill'а в Console.ReadLine.
        Set("YT_SKILL_CHECK", "0");
    }

    /// <summary>
    /// Записывает содержимое в <see cref="ConfigPath"/>.
    /// </summary>
    /// <param name="json">Содержимое файла конфигурации.</param>
    public void SetConfig(string json) => File.WriteAllText(ConfigPath, json);

    /// <summary>
    /// Инжектирует <see cref="HttpMessageHandler"/> в <see cref="TrackerContextFactory"/> через AsyncLocal.
    /// Используется e2e тестами команд, которые внутри <c>SetAction</c> вызывают
    /// <c>TrackerContextFactory.CreateAsync</c> без явного <c>innerHandler</c>.
    /// Сбрасывается автоматически в <see cref="Dispose"/>.
    /// </summary>
    public HttpMessageHandler? InnerHandler
    {
        get => TrackerContextFactory.TestInnerHandlerOverride.Value;
        set => TrackerContextFactory.TestInnerHandlerOverride.Value = value;
    }

    /// <summary>
    /// Инжектирует <see cref="YandexTrackerCLI.Core.Auth.IIamExchangeClient"/>
    /// в <see cref="TrackerContextFactory"/> через AsyncLocal (для service-account профилей).
    /// Сбрасывается автоматически в <see cref="Dispose"/>.
    /// </summary>
    public Core.Auth.IIamExchangeClient? IamExchange
    {
        get => TrackerContextFactory.TestIamExchangeOverride.Value;
        set => TrackerContextFactory.TestIamExchangeOverride.Value = value;
    }

    /// <summary>
    /// Устанавливает переменную окружения (с сохранением старого значения для восстановления).
    /// </summary>
    /// <param name="key">Имя переменной.</param>
    /// <param name="val">Новое значение или <c>null</c> для удаления.</param>
    public void Set(string key, string? val)
    {
        if (!_backup.ContainsKey(key))
        {
            _backup[key] = Environment.GetEnvironmentVariable(key);
        }
        Environment.SetEnvironmentVariable(key, val);
    }

    /// <summary>
    /// Включает/выключает read-only режим через <c>YT_READ_ONLY</c> env-var
    /// (с автоматическим восстановлением в <see cref="Dispose"/>).
    /// </summary>
    /// <param name="enabled">
    /// <c>true</c> — устанавливает <c>YT_READ_ONLY=1</c>; <c>false</c> — сбрасывает переменную.
    /// </param>
    public void SetReadOnly(bool enabled)
    {
        // Использует тот же приём что Set: бэкап + установка env-var.
        Set("YT_READ_ONLY", enabled ? "1" : null);
    }

    /// <summary>
    /// Hook для тестов, позволяющий модифицировать <see cref="InvocationConfiguration"/>
    /// (например, подменить <c>Output</c>/<c>Error</c> или внедрить интерактивные стабы)
    /// непосредственно перед вызовом <c>root.Parse(args).InvokeAsync(cfg)</c>.
    /// </summary>
    public Action<InvocationConfiguration>? ConfigureCli { get; set; }

    /// <summary>
    /// Строит root command, перехватывает <see cref="Console.Out"/>/<see cref="Console.Error"/>
    /// и выполняет команду с переданными аргументами.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    /// <param name="stdout">Writer для stdout.</param>
    /// <param name="stderr">Writer для stderr.</param>
    /// <returns>Exit-code команды.</returns>
    public async Task<int> Invoke(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var prevOut = Console.Out;
        var prevErr = Console.Error;
#pragma warning disable TUnit0055 // Overwriting the Console writer can break TUnit logging — OK в изолированной утилите теста
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var root = RootCommandBuilder.Build();
            var cfg = new InvocationConfiguration { Output = stdout, Error = stderr };
            ConfigureCli?.Invoke(cfg);
            return await root.Parse(args).InvokeAsync(cfg);
        }
        finally
        {
            Console.SetOut(prevOut);
            Console.SetError(prevErr);
        }
#pragma warning restore TUnit0055
    }

    /// <summary>
    /// Восстанавливает переменные окружения, сбрасывает test-инжекции
    /// <see cref="TrackerContextFactory"/> и удаляет временную директорию.
    /// </summary>
    public void Dispose()
    {
        foreach (var (k, v) in _backup)
        {
            Environment.SetEnvironmentVariable(k, v);
        }

        // Всегда сбрасываем test-оверрайды, чтобы они не "протекли" между тестами
        // в рамках одного AsyncLocal-контекста (тесты с [NotInParallel]).
        TrackerContextFactory.TestInnerHandlerOverride.Value = null;
        TrackerContextFactory.TestIamExchangeOverride.Value = null;
        YandexTrackerCLI.Commands.Auth.AuthLoginCommand.TestBrowserLauncher.Value = null;
        YandexTrackerCLI.Commands.Auth.AuthLoginCommand.TestTokenReader.Value = null;
        YandexTrackerCLI.Commands.Auth.AuthLoginCommand.TestFederatedHttpClient.Value = null;

        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }
}
