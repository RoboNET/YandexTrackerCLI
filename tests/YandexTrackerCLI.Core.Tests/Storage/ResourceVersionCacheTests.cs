namespace YandexTrackerCLI.Core.Tests.Storage;

using System.Runtime.InteropServices;
using TUnit.Core;
using YandexTrackerCLI.Core.Config;
using YandexTrackerCLI.Core.Storage;

/// <summary>
/// Тесты <see cref="ResourceVersionCache"/>: round-trip, изоляция ключей,
/// уборка старых записей и устойчивость к битому файлу.
/// </summary>
public sealed class ResourceVersionCacheTests
{
    /// <summary>
    /// Промах на отсутствующем файле — не ошибка.
    /// </summary>
    [Test]
    public async Task Get_WhenFileMissing_ReturnsNull()
    {
        var cache = new ResourceVersionCache(TempPath());

        var entry = await cache.Get(ResourceVersionCache.BuildKey(Profile("default"), "issue", "TECH-1"));

        await Assert.That(entry).IsNull();
    }

    /// <summary>
    /// Записанная версия читается обратно вместе с моментом чтения.
    /// </summary>
    [Test]
    public async Task SetAndGet_RoundTripsVersionAndReadAt()
    {
        var cache = new ResourceVersionCache(TempPath());
        var key = ResourceVersionCache.BuildKey(Profile("default"), "issue", "TECH-1");
        var readAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        await cache.Set(key, 7, readAt);
        var entry = await cache.Get(key);

        await Assert.That(entry!.Version).IsEqualTo(7L);
        await Assert.That((entry.ReadAt - readAt).Duration()).IsLessThan(TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Профиль входит в ключ: одна и та же задача в двух профилях — независимые записи.
    /// </summary>
    [Test]
    public async Task BuildKey_SeparatesProfiles()
    {
        var cache = new ResourceVersionCache(TempPath());
        var work = ResourceVersionCache.BuildKey(Profile("work"), "issue", "TECH-1");
        var home = ResourceVersionCache.BuildKey(Profile("home"), "issue", "TECH-1");

        await cache.Set(work, 3);
        await cache.Set(home, 42);

        await Assert.That((await cache.Get(work))!.Version).IsEqualTo(3L);
        await Assert.That((await cache.Get(home))!.Version).IsEqualTo(42L);
    }

    /// <summary>
    /// Организация входит в ключ: одно и то же имя профиля может указывать на разные
    /// организации (<c>YT_ORG_ID</c>/<c>YT_ORG_TYPE</c> перекрывают профиль, <c>YT_CONFIG_PATH</c>
    /// подменяет конфиг), и версия из одной не должна подставляться в мутацию в другой.
    /// </summary>
    [Test]
    public async Task BuildKey_SeparatesOrganizations()
    {
        var cache = new ResourceVersionCache(TempPath());
        var first = ResourceVersionCache.BuildKey(
            Profile("default", OrgType.Cloud, "org-a"), "issue", "TECH-1");
        var second = ResourceVersionCache.BuildKey(
            Profile("default", OrgType.Cloud, "org-b"), "issue", "TECH-1");

        await cache.Set(first, 3);
        await cache.Set(second, 42);

        await Assert.That((await cache.Get(first))!.Version).IsEqualTo(3L);
        await Assert.That((await cache.Get(second))!.Version).IsEqualTo(42L);
    }

    /// <summary>
    /// Тип организации тоже входит в ключ: один и тот же идентификатор в Яндекс 360 и в
    /// Cloud — разные организации.
    /// </summary>
    [Test]
    public async Task BuildKey_SeparatesOrgTypes()
    {
        var cache = new ResourceVersionCache(TempPath());
        var cloud = ResourceVersionCache.BuildKey(
            Profile("default", OrgType.Cloud, "42"), "issue", "TECH-1");
        var yandex360 = ResourceVersionCache.BuildKey(
            Profile("default", OrgType.Yandex360, "42"), "issue", "TECH-1");

        await cache.Set(cloud, 1);
        await cache.Set(yandex360, 2);

        await Assert.That((await cache.Get(cloud))!.Version).IsEqualTo(1L);
        await Assert.That((await cache.Get(yandex360))!.Version).IsEqualTo(2L);
    }

    /// <summary>
    /// Тип ресурса входит в ключ: триггер 17 и автодействие 17 — разные ресурсы.
    /// </summary>
    [Test]
    public async Task BuildKey_SeparatesResourceTypes()
    {
        var cache = new ResourceVersionCache(TempPath());
        var trigger = ResourceVersionCache.BuildKey(Profile("default"), "trigger", "DEV/17");
        var autoaction = ResourceVersionCache.BuildKey(Profile("default"), "autoaction", "DEV/17");

        await cache.Set(trigger, 1);
        await cache.Set(autoaction, 2);

        await Assert.That((await cache.Get(trigger))!.Version).IsEqualTo(1L);
        await Assert.That((await cache.Get(autoaction))!.Version).IsEqualTo(2L);
    }

    /// <summary>
    /// Запись старше <see cref="ResourceVersionCache.Retention"/> выбрасывается при
    /// следующей записи в кэш.
    /// </summary>
    [Test]
    public async Task Set_DropsEntriesOlderThanRetention()
    {
        var path = TempPath();
        var cache = new ResourceVersionCache(path);
        var stale = ResourceVersionCache.BuildKey(Profile("default"), "issue", "OLD-1");
        var fresh = ResourceVersionCache.BuildKey(Profile("default"), "issue", "NEW-1");
        var now = DateTimeOffset.UtcNow;

        await cache.Set(stale, 1, now - ResourceVersionCache.Retention - TimeSpan.FromDays(1));
        await cache.Set(fresh, 2, now);

        await Assert.That(await cache.Get(stale)).IsNull();
        await Assert.That((await cache.Get(fresh))!.Version).IsEqualTo(2L);
    }

    /// <summary>
    /// Retention — уборка мусора, а не TTL: пока запись жива, она используется независимо
    /// от возраста.
    /// </summary>
    [Test]
    public async Task Get_ReturnsOldEntry_WhenNothingPrunedItYet()
    {
        var cache = new ResourceVersionCache(TempPath());
        var key = ResourceVersionCache.BuildKey(Profile("default"), "issue", "TECH-1");
        var ancient = DateTimeOffset.UtcNow - ResourceVersionCache.Retention - TimeSpan.FromDays(10);

        await cache.Set(key, 5, ancient);

        var entry = await cache.Get(key);

        await Assert.That(entry!.Version).IsEqualTo(5L);
    }

    /// <summary>
    /// Битый файл кэша ведёт себя как промах, а не как ошибка команды.
    /// </summary>
    [Test]
    public async Task Get_WhenFileIsCorrupted_ReturnsNull()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not json at all");
        var cache = new ResourceVersionCache(path);

        var entry = await cache.Get(ResourceVersionCache.BuildKey(Profile("default"), "issue", "TECH-1"));

        await Assert.That(entry).IsNull();
    }

    /// <summary>
    /// Битый файл не мешает записи: она перезаписывает его целиком.
    /// </summary>
    [Test]
    public async Task Set_OverwritesCorruptedFile()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "]]] broken");
        var cache = new ResourceVersionCache(path);
        var key = ResourceVersionCache.BuildKey(Profile("default"), "issue", "TECH-1");

        await cache.Set(key, 9);

        await Assert.That((await cache.Get(key))!.Version).IsEqualTo(9L);
    }

    /// <summary>
    /// Параллельные писатели (как отдельные процессы <c>yt</c>) не портят файл: каждый
    /// со своим экземпляром и своим файловым локом.
    /// </summary>
    [Test]
    public async Task ConcurrentWriters_DoNotCorruptFile()
    {
        var path = TempPath();
        const int writers = 8;

        await Task.WhenAll(Enumerable.Range(0, writers).Select(i => Task.Run(async () =>
        {
            var own = new ResourceVersionCache(path);
            await own.Set(ResourceVersionCache.BuildKey(Profile("default"), "issue", "TECH-" + i), i);
        })));

        var reader = new ResourceVersionCache(path);
        for (var i = 0; i < writers; i++)
        {
            var entry = await reader.Get(ResourceVersionCache.BuildKey(Profile("default"), "issue", "TECH-" + i));
            await Assert.That(entry!.Version).IsEqualTo((long)i);
        }
    }

    /// <summary>
    /// Файл кэша создаётся с правами только для владельца (POSIX).
    /// </summary>
    [Test]
    public async Task Set_WritesUserOnlyPermissions_OnUnix()
    {
        var path = TempPath();
        var cache = new ResourceVersionCache(path);

        await cache.Set(ResourceVersionCache.BuildKey(Profile("default"), "issue", "TECH-1"), 1);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var mode = File.GetUnixFileMode(path);
            await Assert.That(mode & UnixFileMode.GroupRead).IsEqualTo((UnixFileMode)0);
            await Assert.That(mode & UnixFileMode.OtherRead).IsEqualTo((UnixFileMode)0);
        }
    }

    /// <summary>
    /// Путь по умолчанию уважает <c>XDG_CACHE_HOME</c> и лежит рядом с кэшем IAM-токенов.
    /// </summary>
    [Test]
    [NotInParallel("yt-core-env")]
    public async Task DefaultPath_HonoursXdgCacheHome()
    {
        var previous = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var sandbox = Path.Combine(Path.GetTempPath(), "yt-xdg-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", sandbox);

            var path = ResourceVersionCache.DefaultPath;

            await Assert.That(path).IsEqualTo(
                Path.Combine(sandbox, "yandex-tracker", "resource-versions.json"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", previous);
        }
    }

    /// <summary>
    /// Собирает действующий профиль для ключа: значимы только имя и организация.
    /// </summary>
    /// <param name="name">Имя профиля.</param>
    /// <param name="orgType">Тип организации.</param>
    /// <param name="orgId">Идентификатор организации.</param>
    /// <returns>Профиль для <see cref="ResourceVersionCache.BuildKey"/>.</returns>
    private static EffectiveProfile Profile(
        string name, OrgType orgType = OrgType.Cloud, string orgId = "org-1") =>
        new(name, orgType, orgId, ReadOnly: false,
            new AuthConfig(AuthType.OAuth, Token: "y0_X"), ExternalEffectsAllowed: true);

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "yt-cli-versions-" + Guid.NewGuid().ToString("N") + ".json");
}
