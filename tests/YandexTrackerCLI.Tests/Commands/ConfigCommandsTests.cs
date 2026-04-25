namespace YandexTrackerCLI.Tests.Commands;

using System.Text.Json;
using TUnit.Core;

/// <summary>
/// End-to-end тесты команды <c>yt config</c>: list / get / set / profile
/// с проверкой allowlist ключей и маскирования секретов.
/// Тесты мутируют глобальное состояние (<see cref="Console"/>, env vars),
/// поэтому выполняются последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ConfigCommandsTests
{
    private const string TwoProfiles = """
    {"default_profile":"a","profiles":{
      "a":{"org_type":"cloud","org_id":"oa","read_only":false,"auth":{"type":"oauth","token":"y0_a"}},
      "b":{"org_type":"yandex360","org_id":"ob","read_only":true,"auth":{"type":"oauth","token":"y0_b"}}}}
    """;

    /// <summary>
    /// <c>yt config list</c> → JSON-массив имён профилей.
    /// </summary>
    [Test]
    public async Task List_PrintsProfileNames()
    {
        using var env = new TestEnv();
        env.SetConfig(TwoProfiles);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "config", "list" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        var names = doc.RootElement.EnumerateArray().Select(e => e.GetString()!).ToArray();
        await Assert.That(names).IsEquivalentTo(new[] { "a", "b" });
    }

    /// <summary>
    /// <c>yt config get org_id</c> → возвращает строковое значение в JSON.
    /// </summary>
    [Test]
    public async Task Get_OrgId_ReturnsValue()
    {
        using var env = new TestEnv();
        env.SetConfig(TwoProfiles);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "--profile", "a", "config", "get", "org_id" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(sw.ToString().Trim()).IsEqualTo("\"oa\"");
    }

    /// <summary>
    /// <c>yt config get auth.token</c> → значение маскируется как <c>"***"</c>.
    /// </summary>
    [Test]
    public async Task Get_AuthToken_IsMasked()
    {
        using var env = new TestEnv();
        env.SetConfig(TwoProfiles);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "--profile", "a", "config", "get", "auth.token" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(sw.ToString().Trim()).IsEqualTo("\"***\"");
    }

    /// <summary>
    /// Ключ вне allowlist → <c>invalid_args</c> (exit 2).
    /// </summary>
    [Test]
    public async Task Get_UnknownKey_InvalidArgs()
    {
        using var env = new TestEnv();
        env.SetConfig(TwoProfiles);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "--profile", "a", "config", "get", "weird.key" }, sw, er);

        await Assert.That(exit).IsEqualTo(2);
    }

    /// <summary>
    /// <c>yt config set org_id &lt;new&gt;</c> → значение записано в файл.
    /// </summary>
    [Test]
    public async Task Set_OrgId_UpdatesProfile()
    {
        using var env = new TestEnv();
        env.SetConfig(TwoProfiles);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[] { "--profile", "a", "config", "set", "org_id", "new-o" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        await Assert.That(
                doc.RootElement.GetProperty("profiles").GetProperty("a").GetProperty("org_id").GetString())
            .IsEqualTo("new-o");
    }

    /// <summary>
    /// <c>yt config set read_only true</c> → булево значение записано корректно.
    /// </summary>
    [Test]
    public async Task Set_ReadOnly_AcceptsTrueFalse()
    {
        using var env = new TestEnv();
        env.SetConfig(TwoProfiles);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[] { "--profile", "a", "config", "set", "read_only", "true" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        await Assert.That(
                doc.RootElement.GetProperty("profiles").GetProperty("a").GetProperty("read_only").GetBoolean())
            .IsTrue();
    }

    /// <summary>
    /// Попытка <c>set</c> на неизвестный ключ → <c>invalid_args</c> (exit 2).
    /// </summary>
    [Test]
    public async Task Set_UnknownKey_InvalidArgs()
    {
        using var env = new TestEnv();
        env.SetConfig(TwoProfiles);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[] { "--profile", "a", "config", "set", "weird", "whatever" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(2);
    }

    /// <summary>
    /// <c>yt config profile b</c> → <c>default_profile</c> меняется на <c>b</c>.
    /// </summary>
    [Test]
    public async Task Profile_Switches_Default()
    {
        using var env = new TestEnv();
        env.SetConfig(TwoProfiles);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "config", "profile", "b" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        await Assert.That(doc.RootElement.GetProperty("default_profile").GetString()).IsEqualTo("b");
    }

    /// <summary>
    /// <c>yt config profile &lt;нет&gt;</c> → <c>config_error</c> (exit 9).
    /// </summary>
    [Test]
    public async Task Profile_UnknownName_ConfigError()
    {
        using var env = new TestEnv();
        env.SetConfig(TwoProfiles);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "config", "profile", "nope" }, sw, er);

        await Assert.That(exit).IsEqualTo(9);
    }

    /// <summary>
    /// <c>yt config set default_format table</c> → значение записано как строка.
    /// </summary>
    [Test]
    public async Task Set_DefaultFormat_Table_Persisted()
    {
        using var env = new TestEnv();
        env.SetConfig(TwoProfiles);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[] { "--profile", "a", "config", "set", "default_format", "table" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        await Assert.That(
                doc.RootElement.GetProperty("profiles").GetProperty("a").GetProperty("default_format").GetString())
            .IsEqualTo("table");
    }

    /// <summary>
    /// Невалидное значение <c>default_format=xml</c> → <c>invalid_args</c> (exit 2).
    /// </summary>
    [Test]
    public async Task Set_DefaultFormat_Invalid_InvalidArgs()
    {
        using var env = new TestEnv();
        env.SetConfig(TwoProfiles);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[] { "--profile", "a", "config", "set", "default_format", "xml" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(2);
    }

    /// <summary>
    /// <c>yt config get default_format</c> на свежем профиле без default_format → возвращает <c>null</c>.
    /// </summary>
    [Test]
    public async Task Get_DefaultFormat_OnNewProfile_ReturnsNull()
    {
        using var env = new TestEnv();
        env.SetConfig(TwoProfiles);
        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(
            new[] { "--profile", "a", "config", "get", "default_format" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(sw.ToString().Trim()).IsEqualTo("null");
    }
}
