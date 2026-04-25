namespace YandexTrackerCLI.Core.Tests.Auth;

using System.Runtime.InteropServices;
using TUnit.Core;
using YandexTrackerCLI.Core.Auth;

public sealed class TokenCacheTests
{
    [Test]
    public async Task Get_WhenMissing_ReturnsNull()
    {
        var cache = new TokenCache(TempPath());
        var entry = await cache.GetAsync("key");
        await Assert.That(entry).IsNull();
    }

    [Test]
    public async Task SetAndGet_ReturnsValueIfNotExpired()
    {
        var cache = new TokenCache(TempPath());
        var now = DateTimeOffset.UtcNow;
        await cache.SetAsync("key", "iam-token", now.AddHours(1));

        var got = await cache.GetAsync("key", evaluatedAt: now);

        await Assert.That(got!.Token).IsEqualTo("iam-token");
    }

    [Test]
    public async Task Get_WithinLeeway_ReturnsNull()
    {
        // TTL истекает через 30 секунд; leeway = 60 сек; значит trait как expired
        var cache = new TokenCache(TempPath());
        var now = DateTimeOffset.UtcNow;
        await cache.SetAsync("key", "iam-token", now.AddSeconds(30));

        var got = await cache.GetAsync("key", evaluatedAt: now);

        await Assert.That(got).IsNull();
    }

    [Test]
    public async Task Save_SetsFilePermissions_UserOnly_OnUnix()
    {
        var path = TempPath();
        var cache = new TokenCache(path);
        await cache.SetAsync("k", "t", DateTimeOffset.UtcNow.AddHours(1));

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var mode = File.GetUnixFileMode(path);
            await Assert.That(mode & UnixFileMode.GroupRead).IsEqualTo((UnixFileMode)0);
            await Assert.That(mode & UnixFileMode.OtherRead).IsEqualTo((UnixFileMode)0);
        }
    }

    [Test]
    public async Task MultipleKeys_IndependentLifetimes()
    {
        var path = TempPath();
        var cache = new TokenCache(path);
        var now = DateTimeOffset.UtcNow;
        await cache.SetAsync("a", "tok-a", now.AddHours(1));
        await cache.SetAsync("b", "tok-b", now.AddHours(2));
        await cache.SetAsync("a", "tok-a-updated", now.AddHours(3));

        var a = await cache.GetAsync("a", evaluatedAt: now);
        var b = await cache.GetAsync("b", evaluatedAt: now);

        await Assert.That(a!.Token).IsEqualTo("tok-a-updated");
        await Assert.That(b!.Token).IsEqualTo("tok-b");
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "yt-cli-cache-" + Guid.NewGuid().ToString("N") + ".json");
}
