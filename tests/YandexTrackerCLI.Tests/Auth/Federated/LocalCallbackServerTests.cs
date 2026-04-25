namespace YandexTrackerCLI.Tests.Auth.Federated;

using TUnit.Core;
using YandexTrackerCLI.Auth.Federated;

/// <summary>
/// Юнит-тесты <see cref="LocalCallbackServer"/>: единичный callback + таймаут.
/// </summary>
public sealed class LocalCallbackServerTests
{
    [Test]
    public async Task AwaitCallback_ReceivesCodeAndState()
    {
        await using var server = LocalCallbackServer.Start();
        var serverTask = server.AwaitCallbackAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        using var http = new HttpClient();
        using var resp = await http.GetAsync($"http://127.0.0.1:{server.Port}/auth/callback?code=abc&state=xyz");
        await Assert.That((int)resp.StatusCode).IsEqualTo(200);

        var result = await serverTask;
        await Assert.That(result.Code).IsEqualTo("abc");
        await Assert.That(result.State).IsEqualTo("xyz");
        await Assert.That(result.Error).IsNull();
    }

    [Test]
    public async Task AwaitCallback_Timeout_Throws()
    {
        await using var server = LocalCallbackServer.Start();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => server.AwaitCallbackAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }
}
