namespace YandexTrackerCLI.Core.Tests.Auth;

using System.Security.Cryptography;
using TUnit.Core;
using YandexTrackerCLI.Core.Auth;

public sealed class ServiceAccountProviderTests
{
    [Test]
    public async Task FirstCall_Exchanges_AndCaches()
    {
        using var rsa = RSA.Create(2048);
        var fake = new FakeExchange(_ => new IamExchangeResult("iam-1", DateTimeOffset.UtcNow.AddHours(1)));
        var cache = new TokenCache(Path.Combine(Path.GetTempPath(), "yt-sa-" + Guid.NewGuid() + ".json"));
        var provider = new ServiceAccountProvider("sa-1", "key-1", rsa, cache, fake, cacheKey: "t");

        var h = await provider.GetAuthorizationAsync(CancellationToken.None);

        await Assert.That(h.Scheme).IsEqualTo("Bearer");
        await Assert.That(h.Parameter).IsEqualTo("iam-1");
        await Assert.That(fake.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task SecondCallWithinTtl_HitsCache_NoExchange()
    {
        using var rsa = RSA.Create(2048);
        var fake = new FakeExchange(_ => new IamExchangeResult("iam-2", DateTimeOffset.UtcNow.AddHours(1)));
        var cache = new TokenCache(Path.Combine(Path.GetTempPath(), "yt-sa-" + Guid.NewGuid() + ".json"));
        var provider = new ServiceAccountProvider("sa-1", "key-1", rsa, cache, fake, cacheKey: "t");

        _ = await provider.GetAuthorizationAsync(CancellationToken.None);
        _ = await provider.GetAuthorizationAsync(CancellationToken.None);

        await Assert.That(fake.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task DifferentCacheKeys_TriggerSeparateExchanges()
    {
        using var rsa = RSA.Create(2048);
        var fake = new FakeExchange(_ => new IamExchangeResult("tok", DateTimeOffset.UtcNow.AddHours(1)));
        var cache = new TokenCache(Path.Combine(Path.GetTempPath(), "yt-sa-" + Guid.NewGuid() + ".json"));
        var p1 = new ServiceAccountProvider("sa-1", "key-1", rsa, cache, fake, cacheKey: "k1");
        var p2 = new ServiceAccountProvider("sa-1", "key-2", rsa, cache, fake, cacheKey: "k2");

        _ = await p1.GetAuthorizationAsync(CancellationToken.None);
        _ = await p2.GetAuthorizationAsync(CancellationToken.None);

        await Assert.That(fake.CallCount).IsEqualTo(2);
    }

    private sealed class FakeExchange : IIamExchangeClient
    {
        private readonly Func<string, IamExchangeResult> _impl;
        public int CallCount { get; private set; }
        public FakeExchange(Func<string, IamExchangeResult> impl) => _impl = impl;
        public Task<IamExchangeResult> ExchangeAsync(string jwt, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_impl(jwt));
        }
    }
}
