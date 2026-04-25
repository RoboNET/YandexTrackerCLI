using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты cascade-резолва формата: проверяют, что CLI <c>--format</c>,
/// env <c>YT_FORMAT</c> и <c>profile.default_format</c> корректно влияют на вывод
/// команды <c>yt user me</c>.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class FormatIntegrationTests
{
    private const string DefaultConfig =
        """{"default_profile":"a","profiles":{"a":{"org_type":"cloud","org_id":"o","read_only":false,"auth":{"type":"oauth","token":"y0_X"}}}}""";

    private const string ProfileWithTableFormat =
        """{"default_profile":"a","profiles":{"a":{"org_type":"cloud","org_id":"o","read_only":false,"default_format":"table","auth":{"type":"oauth","token":"y0_X"}}}}""";

    private static TestHttpMessageHandler CreateMockUser() =>
        new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"self":"https://.../myself","login":"me","uid":42}""",
                    Encoding.UTF8,
                    "application/json"),
            };
            return r;
        });

    [Test]
    public async Task CliMinimal_OverridesEverything_PrintsLoginOnly()
    {
        using var env = new TestEnv();
        env.SetConfig(ProfileWithTableFormat);
        env.InnerHandler = CreateMockUser();

        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "--format", "minimal", "user", "me" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        // Minimal: identifying field — `login` (key/id отсутствуют, login есть).
        var output = sw.ToString().TrimEnd('\r', '\n');
        await Assert.That(output).IsEqualTo("me");
    }

    [Test]
    public async Task EnvFormat_AppliesWhen_CliIsAuto()
    {
        using var env = new TestEnv();
        env.SetConfig(DefaultConfig);
        env.Set("YT_FORMAT", "minimal");
        env.InnerHandler = CreateMockUser();

        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "user", "me" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        var output = sw.ToString().TrimEnd('\r', '\n');
        await Assert.That(output).IsEqualTo("me");
    }

    [Test]
    public async Task ProfileDefaultFormat_AppliesWhen_NoCliAndNoEnv()
    {
        using var env = new TestEnv();
        env.SetConfig(ProfileWithTableFormat);
        env.InnerHandler = CreateMockUser();

        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "user", "me" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        // Format=table в TTY, в тестах stdout перенаправлен → resolver поднимает Table из профиля.
        var output = sw.ToString();
        // Table-вывод содержит юникод-разделитель и login=me.
        await Assert.That(output).Contains("─");
        await Assert.That(output).Contains("me");
    }

    [Test]
    public async Task CliJson_DefeatsProfileTable()
    {
        using var env = new TestEnv();
        env.SetConfig(ProfileWithTableFormat);
        env.InnerHandler = CreateMockUser();

        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "--format", "json", "user", "me" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        // CLI=json побеждает profile=table → валидный JSON.
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("login").GetString()).IsEqualTo("me");
    }

    [Test]
    public async Task InvalidEnvFormat_FailsWith_InvalidArgs()
    {
        using var env = new TestEnv();
        env.SetConfig(DefaultConfig);
        env.Set("YT_FORMAT", "yaml");
        env.InnerHandler = CreateMockUser();

        var sw = new StringWriter();
        var er = new StringWriter();

        var exit = await env.Invoke(new[] { "user", "me" }, sw, er);

        // Невалидный YT_FORMAT → exit 2 (InvalidArgs).
        await Assert.That(exit).IsEqualTo(2);
    }
}
