namespace YandexTrackerCLI.Tests.Commands;

using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Commands.Auth;
using YandexTrackerCLI.Interactive;

/// <summary>
/// End-to-end тесты интерактивного OAuth-flow команды <c>yt auth login --type oauth</c>
/// без <c>--token</c>. Используют фейковые <see cref="IBrowserLauncher"/> и
/// <see cref="ITokenReader"/>, инжектируемые через AsyncLocal-override.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class AuthLoginInteractiveTests
{
    /// <summary>
    /// Фейковый браузер: запоминает последний URL и ничего не запускает.
    /// </summary>
    private sealed class FakeBrowser : IBrowserLauncher
    {
        public string? Url { get; private set; }

        public Task OpenAsync(string url, CancellationToken ct)
        {
            Url = url;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Фейковый stdin-reader с пред-заданной очередью строк и флагом "redirected".
    /// </summary>
    private sealed class FakeReader : ITokenReader
    {
        private readonly Queue<string?> _lines;

        public bool IsInputRedirected { get; }

        public FakeReader(bool isRedirected, params string?[] lines)
        {
            IsInputRedirected = isRedirected;
            _lines = new Queue<string?>(lines);
        }

        public string? ReadLine() => _lines.Count > 0 ? _lines.Dequeue() : null;
    }

    /// <summary>
    /// TTY + пустой <c>--token</c>: открывается браузер, токен читается со stdin,
    /// профиль сохраняется с <c>type=oauth</c>.
    /// </summary>
    [Test]
    public async Task OAuth_Interactive_OpensBrowser_ReadsToken_SavesProfile()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");
        var fakeBrowser = new FakeBrowser();
        var fakeReader = new FakeReader(isRedirected: false, "y0_MYTOKEN");

        AuthLoginCommand.TestBrowserLauncher.Value = fakeBrowser;
        AuthLoginCommand.TestTokenReader.Value = fakeReader;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[]
            {
                "--profile", "work",
                "auth", "login", "--type", "oauth",
                "--org-type", "cloud", "--org-id", "o1",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(fakeBrowser.Url).IsNotNull();
        await Assert.That(fakeBrowser.Url!).Contains("oauth.yandex.ru/authorize");
        await Assert.That(fakeBrowser.Url!).Contains("response_type=token");

        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var token = saved.RootElement
            .GetProperty("profiles").GetProperty("work")
            .GetProperty("auth").GetProperty("token").GetString();
        await Assert.That(token).IsEqualTo("y0_MYTOKEN");
    }

    /// <summary>
    /// stdin redirected (non-TTY) и нет <c>--token</c> → <c>invalid_args</c> (exit 2).
    /// </summary>
    [Test]
    public async Task OAuth_NonTTY_WithoutToken_Returns_Exit2()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");
        AuthLoginCommand.TestTokenReader.Value = new FakeReader(isRedirected: true);
        AuthLoginCommand.TestBrowserLauncher.Value = new FakeBrowser();

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "auth", "login", "--type", "oauth", "--org-type", "cloud", "--org-id", "o" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(2);
    }

    /// <summary>
    /// Три попытки пустого/whitespace ввода → <c>auth_failed</c> (exit 4).
    /// </summary>
    [Test]
    public async Task OAuth_Interactive_AllAttemptsEmpty_Returns_AuthFailed()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");
        AuthLoginCommand.TestTokenReader.Value = new FakeReader(isRedirected: false, "", "  ", null);
        AuthLoginCommand.TestBrowserLauncher.Value = new FakeBrowser();

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "auth", "login", "--type", "oauth", "--org-type", "cloud", "--org-id", "o" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(4);
    }

    /// <summary>
    /// <c>--client-id myclient</c> → появляется в authorize URL.
    /// </summary>
    [Test]
    public async Task OAuth_Interactive_ClientId_FromCli_UsedInUrl()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"default","profiles":{}}""");
        var fakeBrowser = new FakeBrowser();
        AuthLoginCommand.TestBrowserLauncher.Value = fakeBrowser;
        AuthLoginCommand.TestTokenReader.Value = new FakeReader(isRedirected: false, "y0_X");

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[]
            {
                "auth", "login", "--type", "oauth", "--client-id", "myclient",
                "--org-type", "cloud", "--org-id", "o",
            },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(fakeBrowser.Url!).Contains("client_id=myclient");
    }
}
