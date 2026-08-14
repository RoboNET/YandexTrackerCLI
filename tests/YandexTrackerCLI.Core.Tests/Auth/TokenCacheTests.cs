namespace YandexTrackerCLI.Core.Tests.Auth;

using System.Runtime.InteropServices;
using TUnit.Core;
using YandexTrackerCLI.Core.Api.Errors;
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

    /// <summary>
    /// Записи в кэш идут при каждом обновлении токена, поэтому два <c>yt</c>, пишущих
    /// одновременно, — не теоретический случай. Внутрипроцессный семафор их не разводит:
    /// разводит файловый лок, и ни одна запись не должна потеряться.
    /// </summary>
    [Test]
    public async Task SetAsync_ConcurrentWritersAcrossInstances_LoseNoEntry()
    {
        var path = TempPath();
        var now = DateTimeOffset.UtcNow;

        const int writers = 8;
        await Task.WhenAll(Enumerable.Range(0, writers).Select(i => Task.Run(async () =>
        {
            // Свой экземпляр = свой файловый дескриптор лока, как у отдельного процесса.
            var cache = new TokenCache(path);
            await cache.SetAsync($"k{i}", $"tok-{i}", now.AddHours(1));
        })));

        var reader = new TokenCache(path);
        for (var i = 0; i < writers; i++)
        {
            var entry = await reader.GetAsync($"k{i}", evaluatedAt: now);
            await Assert.That(entry!.Token).IsEqualTo($"tok-{i}");
        }
    }

    [Test]
    public async Task SetAsync_WhenLockIsHeld_FailsWithConfigError_InsteadOfHanging()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var cache = new TokenCache(path, lockTimeout: TimeSpan.FromMilliseconds(200));

        using var holder = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        TrackerException? caught = null;
        try
        {
            await cache.SetAsync("k", "t", DateTimeOffset.UtcNow.AddHours(1));
        }
        catch (TrackerException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Code).IsEqualTo(ErrorCode.ConfigError);
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "yt-cli-cache-" + Guid.NewGuid().ToString("N") + ".json");
}
