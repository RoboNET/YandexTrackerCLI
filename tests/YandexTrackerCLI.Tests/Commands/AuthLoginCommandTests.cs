namespace YandexTrackerCLI.Tests.Commands;

using System.Text.Json;
using TUnit.Core;

/// <summary>
/// End-to-end тесты команды <c>yt auth login</c> (non-interactive): три типа
/// аутентификации (oauth, iam-static, service-account) + валидационные ошибки.
/// Тесты мутируют глобальное состояние (<see cref="Console"/> и переменные окружения),
/// поэтому должны выполняться последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class AuthLoginCommandTests
{
    /// <summary>
    /// OAuth + <c>--token</c> → записывает профиль в конфиг, возвращает exit 0.
    /// </summary>
    [Test]
    public async Task Login_OAuth_WithToken_StoresProfile()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[]
            {
                "--profile", "work",
                "auth", "login", "--type", "oauth", "--token", "y0_X",
                "--org-type", "cloud", "--org-id", "o1",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);

        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var work = saved.RootElement.GetProperty("profiles").GetProperty("work");
        await Assert.That(work.GetProperty("auth").GetProperty("type").GetString()).IsEqualTo("oauth");
        await Assert.That(work.GetProperty("auth").GetProperty("token").GetString()).IsEqualTo("y0_X");
        await Assert.That(work.GetProperty("org_type").GetString()).IsEqualTo("cloud");
        await Assert.That(work.GetProperty("org_id").GetString()).IsEqualTo("o1");
    }

    /// <summary>
    /// OAuth без <c>--token</c> → ошибка <c>invalid_args</c> (exit 2).
    /// </summary>
    [Test]
    public async Task Login_OAuth_WithoutToken_InvalidArgs()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[]
            {
                "auth", "login", "--type", "oauth",
                "--org-type", "cloud", "--org-id", "o1",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(2);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("invalid_args");
    }

    /// <summary>
    /// IAM-static + <c>--token</c> → записывает профиль с <c>type=iam-static</c>.
    /// </summary>
    [Test]
    public async Task Login_IamStatic_WithToken_StoresProfile()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[]
            {
                "--profile", "ci",
                "auth", "login", "--type", "iam-static", "--token", "t1.IAM",
                "--org-type", "cloud", "--org-id", "o1",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var ci = doc.RootElement.GetProperty("profiles").GetProperty("ci");
        await Assert.That(ci.GetProperty("auth").GetProperty("type").GetString()).IsEqualTo("iam-static");
        await Assert.That(ci.GetProperty("auth").GetProperty("token").GetString()).IsEqualTo("t1.IAM");
    }

    /// <summary>
    /// Service-account с <c>--key-file</c> → записывает профиль с путём к PEM.
    /// </summary>
    [Test]
    public async Task Login_ServiceAccount_WithKeyFile_StoresProfile()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[]
            {
                "--profile", "ci-sa",
                "auth", "login", "--type", "service-account",
                "--sa-id", "sa-1", "--key-id", "k-1", "--key-file", "/secrets/sa.pem",
                "--org-type", "cloud", "--org-id", "o1",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var sa = doc.RootElement.GetProperty("profiles").GetProperty("ci-sa").GetProperty("auth");
        await Assert.That(sa.GetProperty("type").GetString()).IsEqualTo("service-account");
        await Assert.That(sa.GetProperty("service_account_id").GetString()).IsEqualTo("sa-1");
        await Assert.That(sa.GetProperty("key_id").GetString()).IsEqualTo("k-1");
        await Assert.That(sa.GetProperty("private_key_path").GetString()).IsEqualTo("/secrets/sa.pem");
    }

    /// <summary>
    /// Service-account без <c>--key-file</c>/<c>--key-pem</c> → ошибка валидации (exit 2).
    /// </summary>
    [Test]
    public async Task Login_ServiceAccount_MissingKey_InvalidArgs()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[]
            {
                "auth", "login", "--type", "service-account",
                "--sa-id", "sa-1", "--key-id", "k-1",
                "--org-type", "cloud", "--org-id", "o1",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(2);
    }

    /// <summary>
    /// Глобальный флаг <c>--read-only</c> при <c>auth login</c> записывается в профиль:
    /// профиль для автоматики сразу заводится только на чтение (issue #13).
    /// </summary>
    [Test]
    [Arguments("oauth")]
    [Arguments("iam-static")]
    public async Task Login_WithReadOnlyFlag_StoresReadOnlyProfile(string type)
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[]
            {
                "--profile", "ci", "--read-only",
                "auth", "login", "--type", type, "--token", "y0_X",
                "--org-type", "cloud", "--org-id", "o1",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        await Assert.That(saved.RootElement.GetProperty("profiles").GetProperty("ci")
            .GetProperty("read_only").GetBoolean()).IsTrue();
    }

    /// <summary>
    /// Service-account профиль тоже поддерживает <c>--read-only</c>.
    /// </summary>
    [Test]
    public async Task Login_ServiceAccount_WithReadOnlyFlag_StoresReadOnlyProfile()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[]
            {
                "--profile", "ci", "--read-only",
                "auth", "login", "--type", "service-account",
                "--sa-id", "aje1", "--key-id", "ajk1", "--key-pem", "PEM",
                "--org-type", "cloud", "--org-id", "o1",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var ci = saved.RootElement.GetProperty("profiles").GetProperty("ci");
        await Assert.That(ci.GetProperty("read_only").GetBoolean()).IsTrue();
        await Assert.That(ci.GetProperty("auth").GetProperty("type").GetString()).IsEqualTo("service-account");
    }

    /// <summary>
    /// Login в существующий профиль пересоздаёт его: политики берутся только из флагов
    /// текущего вызова, поэтому <c>read_only</c>, <c>allowed_queues</c> и
    /// <c>allowed_write_issues</c> сбрасываются. Это заявленный путь сброса политики —
    /// доступный лишь тому, у кого есть сами креденшелы. <c>default_format</c>
    /// (настройка вывода, не граница доступа) переносится.
    /// </summary>
    [Test]
    public async Task Login_IntoExistingProfile_ResetsProfilePolicies()
    {
        using var env = new TestEnv();
        env.SetConfig("""
        {
          "default_profile":"ci",
          "profiles":{"ci":{"org_type":"cloud","org_id":"o","read_only":true,
                            "default_format":"json",
                            "allowed_queues":["DEV"],
                            "allowed_write_issues":["DEV-1"],
                            "auth":{"type":"oauth","token":"old"}}}
        }
        """);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[]
            {
                "--profile", "ci",
                "auth", "login", "--type", "oauth", "--token", "y0_NEW",
                "--org-type", "cloud", "--org-id", "o1",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var ci = saved.RootElement.GetProperty("profiles").GetProperty("ci");
        await Assert.That(ci.GetProperty("auth").GetProperty("token").GetString()).IsEqualTo("y0_NEW");
        await Assert.That(ci.GetProperty("read_only").GetBoolean()).IsFalse();
        await Assert.That(ci.TryGetProperty("allowed_queues", out var q) && q.ValueKind != JsonValueKind.Null)
            .IsFalse();
        await Assert.That(ci.TryGetProperty("allowed_write_issues", out var w) && w.ValueKind != JsonValueKind.Null)
            .IsFalse();
        await Assert.That(ci.GetProperty("default_format").GetString()).IsEqualTo("json");
    }

    /// <summary>
    /// Сброс через login — это именно пересоздание профиля: после него ограниченный
    /// профиль снова ходит в любую очередь, а <c>--read-only</c> при том же вызове
    /// заводит профиль только на чтение.
    /// </summary>
    [Test]
    public async Task Login_IntoExistingProfile_WithReadOnlyFlag_ReCreatesProfileAsReadOnly()
    {
        using var env = new TestEnv();
        env.SetConfig("""
        {
          "default_profile":"ci",
          "profiles":{"ci":{"org_type":"cloud","org_id":"o","read_only":false,
                            "allowed_queues":["DEV"],
                            "auth":{"type":"oauth","token":"old"}}}
        }
        """);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[]
            {
                "--profile", "ci", "--read-only",
                "auth", "login", "--type", "oauth", "--token", "y0_NEW",
                "--org-type", "cloud", "--org-id", "o1",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var ci = saved.RootElement.GetProperty("profiles").GetProperty("ci");
        await Assert.That(ci.GetProperty("read_only").GetBoolean()).IsTrue();
        await Assert.That(ci.TryGetProperty("allowed_queues", out var q) && q.ValueKind != JsonValueKind.Null)
            .IsFalse();
    }

    /// <summary>
    /// <c>--read-only</c> — глобальная (recursive) опция, поэтому принимается и после
    /// подкоманды: <c>yt auth login ... --read-only</c> эквивалентно <c>yt --read-only auth login ...</c>.
    /// </summary>
    [Test]
    public async Task Login_ReadOnlyFlag_AfterSubcommand_IsAccepted()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[]
            {
                "auth", "login", "--profile", "ci", "--type", "oauth", "--token", "y0_X",
                "--org-type", "cloud", "--org-id", "o1", "--read-only",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        await Assert.That(saved.RootElement.GetProperty("profiles").GetProperty("ci")
            .GetProperty("read_only").GetBoolean()).IsTrue();
    }
}
