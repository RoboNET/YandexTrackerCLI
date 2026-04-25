namespace YandexTrackerCLI.Core.Tests.Http;

using System.Net;
using TUnit.Core;
using YandexTrackerCLI.Core.Http;

/// <summary>
/// Юнит-тесты <see cref="DPoPHandler"/>: no-op при отсутствии фабрики,
/// установка <c>DPoP:</c> header при её наличии.
/// </summary>
public sealed class DPoPHandlerTests
{
    [Test]
    public async Task NoFactorySet_PassesThrough_WithoutDpopHeader()
    {
        DPoPHandler.ProofFactory.Value = null;
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new DPoPHandler() { InnerHandler = inner };
        using var client = new HttpClient(handler);

        using var _ = await client.GetAsync("https://api.example.test/x");

        var seen = inner.Seen[0];
        await Assert.That(seen.Headers.Contains("DPoP")).IsFalse();
    }

    [Test]
    public async Task FactorySet_AttachesDpopHeader_FromFactory()
    {
        try
        {
            DPoPHandler.ProofFactory.Value = (method, url) => $"proof[{method}:{url}]";
            var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var handler = new DPoPHandler() { InnerHandler = inner };
            using var client = new HttpClient(handler);

            using var _ = await client.GetAsync("https://api.example.test/items/1");

            var seen = inner.Seen[0];
            await Assert.That(seen.Headers.Contains("DPoP")).IsTrue();
            var value = seen.Headers.GetValues("DPoP").Single();
            await Assert.That(value).IsEqualTo("proof[GET:https://api.example.test/items/1]");
        }
        finally
        {
            DPoPHandler.ProofFactory.Value = null;
        }
    }
}
