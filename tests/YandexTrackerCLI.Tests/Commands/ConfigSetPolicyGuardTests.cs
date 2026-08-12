namespace YandexTrackerCLI.Tests.Commands;

using System.Text.Json;
using TUnit.Core;

/// <summary>
/// Тесты правила «<c>yt config set</c> может только ужесточить политику профиля».
/// Смысл: границу, которую утилита держит против собственного вызывающего, нельзя снимать
/// той же командой, которой этот вызывающий располагает. Мутируют глобальный state
/// (env + Console), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ConfigSetPolicyGuardTests
{
    private const string ReadOnlyProfile =
        """
        {
          "default_profile":"ci",
          "profiles":{"ci":{"org_type":"cloud","org_id":"o","read_only":true,
                            "auth":{"type":"oauth","token":"y0_X"}}}
        }
        """;

    private const string RestrictedProfile =
        """
        {
          "default_profile":"ci",
          "profiles":{"ci":{"org_type":"cloud","org_id":"o","read_only":false,
                            "allowed_queues":["DEV","QA"],
                            "allowed_write_issues":["DEV-1","DEV-2"],
                            "auth":{"type":"oauth","token":"y0_X"}}}
        }
        """;

    private static async Task<(int Exit, string Stdout, string Stderr)> Run(TestEnv env, params string[] args)
    {
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(args, sw, er);
        return (exit, sw.ToString(), er.ToString());
    }

    private static async Task AssertPolicyViolation(string stderr)
    {
        using var doc = JsonDocument.Parse(stderr);
        var error = doc.RootElement.GetProperty("error");
        await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("policy_violation");
        // Отказ обязан назвать путь сброса, иначе пользователь застревает.
        await Assert.That(error.GetProperty("message").GetString()!).Contains("yt auth login");
    }

    /// <summary>
    /// <c>read_only true → false</c> запрещено.
    /// </summary>
    [Test]
    public async Task ConfigSet_ReadOnly_CannotBeTurnedOff()
    {
        using var env = new TestEnv();
        env.SetConfig(ReadOnlyProfile);

        var (exit, _, stderr) = await Run(env, "config", "set", "read_only", "false");

        await Assert.That(exit).IsEqualTo(10);
        await AssertPolicyViolation(stderr);

        // Файл конфига не изменился.
        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        await Assert.That(saved.RootElement.GetProperty("profiles").GetProperty("ci")
            .GetProperty("read_only").GetBoolean()).IsTrue();
    }

    /// <summary>
    /// <c>read_only false → true</c> — ужесточение, проходит.
    /// </summary>
    [Test]
    public async Task ConfigSet_ReadOnly_CanBeTurnedOn()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var (exit, _, _) = await Run(env, "config", "set", "read_only", "true");

        await Assert.That(exit).IsEqualTo(0);
        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        await Assert.That(saved.RootElement.GetProperty("profiles").GetProperty("default")
            .GetProperty("read_only").GetBoolean()).IsTrue();
    }

    /// <summary>
    /// Снятие <c>allowed_queues</c> пустой строкой запрещено.
    /// </summary>
    [Test]
    public async Task ConfigSet_AllowedQueues_CannotBeCleared()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedProfile);

        var (exit, _, stderr) = await Run(env, "config", "set", "allowed_queues", string.Empty);

        await Assert.That(exit).IsEqualTo(10);
        await AssertPolicyViolation(stderr);
    }

    /// <summary>
    /// Добавление очереди, которой нет в текущем списке, запрещено — даже вместе
    /// с сохранением уже разрешённых.
    /// </summary>
    [Test]
    public async Task ConfigSet_AllowedQueues_CannotBeWidened()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedProfile);

        var (exit, _, stderr) = await Run(env, "config", "set", "allowed_queues", "DEV,QA,OPS");

        await Assert.That(exit).IsEqualTo(10);
        await AssertPolicyViolation(stderr);

        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var queues = saved.RootElement.GetProperty("profiles").GetProperty("ci")
            .GetProperty("allowed_queues").EnumerateArray().Select(e => e.GetString()!).ToArray();
        await Assert.That(queues).IsEquivalentTo(new[] { "DEV", "QA" });
    }

    /// <summary>
    /// Сужение списка очередей проходит — это ужесточение.
    /// </summary>
    [Test]
    public async Task ConfigSet_AllowedQueues_CanBeNarrowed()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedProfile);

        var (exit, _, _) = await Run(env, "config", "set", "allowed_queues", "DEV");

        await Assert.That(exit).IsEqualTo(0);
        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var queues = saved.RootElement.GetProperty("profiles").GetProperty("ci")
            .GetProperty("allowed_queues").EnumerateArray().Select(e => e.GetString()!).ToArray();
        await Assert.That(queues).IsEquivalentTo(new[] { "DEV" });
    }

    /// <summary>
    /// Снятие <c>allowed_write_issues</c> запрещено.
    /// </summary>
    [Test]
    public async Task ConfigSet_AllowedWriteIssues_CannotBeCleared()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedProfile);

        var (exit, _, stderr) = await Run(env, "config", "set", "allowed_write_issues", string.Empty);

        await Assert.That(exit).IsEqualTo(10);
        await AssertPolicyViolation(stderr);
    }

    /// <summary>
    /// Добавление задачи вне текущего списка запрещено, сужение — проходит.
    /// </summary>
    [Test]
    public async Task ConfigSet_AllowedWriteIssues_OnlyNarrowingIsAllowed()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedProfile);

        var (widen, _, stderr) = await Run(env, "config", "set", "allowed_write_issues", "DEV-1,DEV-9");
        await Assert.That(widen).IsEqualTo(10);
        await AssertPolicyViolation(stderr);

        var (narrow, _, _) = await Run(env, "config", "set", "allowed_write_issues", "DEV-2");
        await Assert.That(narrow).IsEqualTo(0);

        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var issues = saved.RootElement.GetProperty("profiles").GetProperty("ci")
            .GetProperty("allowed_write_issues").EnumerateArray().Select(e => e.GetString()!).ToArray();
        await Assert.That(issues).IsEquivalentTo(new[] { "DEV-2" });
    }

    /// <summary>
    /// Регистр не даёт обхода: <c>dev</c> — это та же очередь <c>DEV</c>, значит сужение.
    /// </summary>
    [Test]
    public async Task ConfigSet_AllowedQueues_NarrowingIsCaseInsensitive()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedProfile);

        var (exit, _, _) = await Run(env, "config", "set", "allowed_queues", "dev");

        await Assert.That(exit).IsEqualTo(0);
    }

    /// <summary>
    /// Пока ограничения нет, установить любой список можно — это ужесточение.
    /// </summary>
    [Test]
    public async Task ConfigSet_AllowedQueues_CanBeSetWhenUnrestricted()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var (exit, _, _) = await Run(env, "config", "set", "allowed_queues", "DEV,QA");

        await Assert.That(exit).IsEqualTo(0);
    }

    /// <summary>
    /// Полный путь сброса: <c>yt auth login</c> в тот же профиль пересоздаёт его,
    /// и после этого ограничение действительно снято.
    /// </summary>
    [Test]
    public async Task Login_IsTheDocumentedResetPath_ForAllowedQueues()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedProfile);

        var (blocked, _, _) = await Run(env, "config", "set", "allowed_queues", string.Empty);
        await Assert.That(blocked).IsEqualTo(10);

        var (login, _, _) = await Run(
            env,
            "--profile", "ci",
            "auth", "login", "--type", "oauth", "--token", "y0_NEW",
            "--org-type", "cloud", "--org-id", "o");
        await Assert.That(login).IsEqualTo(0);

        var (get, stdout, _) = await Run(env, "config", "get", "allowed_queues");
        await Assert.That(get).IsEqualTo(0);
        using var doc = JsonDocument.Parse(stdout);
        await Assert.That(doc.RootElement.GetString()).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// Барьер держится на маскировании креденшелов: <c>yt config get auth.token</c> не
    /// отдаёт токен, поэтому «прочитать и залогиниться заново» из-под ограниченного
    /// профиля не получится.
    /// </summary>
    [Test]
    public async Task ConfigGet_AuthToken_IsMasked()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedProfile);

        var (exit, stdout, _) = await Run(env, "config", "get", "auth.token");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(stdout).DoesNotContain("y0_X");
    }
}
