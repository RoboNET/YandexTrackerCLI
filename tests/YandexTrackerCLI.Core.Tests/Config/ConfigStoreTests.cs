namespace YandexTrackerCLI.Core.Tests.Config;

using System.Runtime.InteropServices;
using TUnit.Core;
using YandexTrackerCLI.Core.Api.Errors;
using YandexTrackerCLI.Core.Config;

public sealed class ConfigStoreTests
{
    [Test]
    public async Task Load_MissingFile_ReturnsEmptyConfig()
    {
        var dir = CreateTempDir();
        var store = new ConfigStore(Path.Combine(dir, "config.json"));

        var cfg = await store.LoadAsync();

        await Assert.That(cfg.DefaultProfile).IsEqualTo("default");
        await Assert.That(cfg.Profiles).IsEmpty();
    }

    [Test]
    public async Task SaveThenLoad_PreservesProfiles()
    {
        var dir = CreateTempDir();
        var path = Path.Combine(dir, "config.json");
        var store = new ConfigStore(path);

        var original = new ConfigFile("work", new()
        {
            ["work"] = new Profile(OrgType.Cloud, "org-1", false,
                new AuthConfig(AuthType.OAuth, Token: "y0_X")),
        });
        await store.SaveAsync(original);
        var back = await store.LoadAsync();

        await Assert.That(back.DefaultProfile).IsEqualTo("work");
        await Assert.That(back.Profiles["work"].Auth.Token).IsEqualTo("y0_X");

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var mode = File.GetUnixFileMode(path);
            await Assert.That(mode & UnixFileMode.GroupRead).IsEqualTo((UnixFileMode)0);
            await Assert.That(mode & UnixFileMode.OtherRead).IsEqualTo((UnixFileMode)0);
        }
    }

    [Test]
    public async Task SaveAsync_CreatesDirectoryTree_IfMissing()
    {
        var dir = CreateTempDir();
        var path = Path.Combine(dir, "nested", "deep", "config.json");
        var store = new ConfigStore(path);

        await store.SaveAsync(new ConfigFile("d", new Dictionary<string, Profile>()));

        await Assert.That(File.Exists(path)).IsTrue();
    }

    /// <summary>
    /// Гонка read-modify-write: правка, легшая на диск между чтением снимка и записью,
    /// обязана пережить запись. Пара <c>LoadAsync</c> + <c>SaveAsync</c> её теряет — именно
    /// поэтому все точки записи ходят через <c>ModifyAsync</c>.
    /// </summary>
    [Test]
    public async Task ModifyAsync_AppliesMutationToFreshSnapshot_NotToStaleOne()
    {
        var path = Path.Combine(CreateTempDir(), "config.json");
        var store = new ConfigStore(path);
        await store.SaveAsync(new ConfigFile("a", new()
        {
            ["a"] = Oauth("token-a"),
        }));

        // Снимок, который «команда» держит в памяти, пока идёт долгая операция.
        var stale = await store.LoadAsync();

        // Другой процесс успевает и добавить профиль, и переключить default.
        await store.SaveAsync(new ConfigFile("b", new(stale.Profiles)
        {
            ["b"] = Oauth("token-b"),
        }));

        await store.ModifyAsync(fresh => new ConfigFile(
            fresh.DefaultProfile,
            new Dictionary<string, Profile>(fresh.Profiles) { ["a"] = Oauth("token-a-updated") }));

        var result = await store.LoadAsync();
        await Assert.That(result.DefaultProfile).IsEqualTo("b");
        await Assert.That(result.Profiles.ContainsKey("b")).IsTrue();
        await Assert.That(result.Profiles["b"].Auth.Token).IsEqualTo("token-b");
        await Assert.That(result.Profiles["a"].Auth.Token).IsEqualTo("token-a-updated");
    }

    [Test]
    public async Task ModifyAsync_ConcurrentUpdates_LoseNothing()
    {
        var path = Path.Combine(CreateTempDir(), "config.json");
        await new ConfigStore(path).SaveAsync(new ConfigFile("p0", new Dictionary<string, Profile>()));

        const int writers = 8;
        var tasks = Enumerable.Range(0, writers).Select(i => Task.Run(async () =>
        {
            // Отдельный экземпляр на «процесс»: лок берётся на своём файловом дескрипторе.
            var store = new ConfigStore(path);
            await store.ModifyAsync(fresh => new ConfigFile(
                fresh.DefaultProfile,
                new Dictionary<string, Profile>(fresh.Profiles) { [$"p{i}"] = Oauth($"token-{i}") }));
        }));

        await Task.WhenAll(tasks);

        var result = await new ConfigStore(path).LoadAsync();
        await Assert.That(result.Profiles).Count().IsEqualTo(writers);
        for (var i = 0; i < writers; i++)
        {
            await Assert.That(result.Profiles[$"p{i}"].Auth.Token).IsEqualTo($"token-{i}");
        }
    }

    /// <summary>
    /// Одновременные записи не должны делить временный файл: фиксированное имя
    /// <c>config.json.tmp</c> означало бы, что писатели топчут промежуточное состояние
    /// друг друга, а на диске остаётся обрезанный файл с креденшелами всех профилей.
    /// </summary>
    /// <remarks>
    /// <c>SaveAsync</c> идёт без лока, поэтому «все шестнадцать записей удались» здесь не
    /// гарантируется и в проде: на Windows замена целевого файла отказывает, пока его
    /// заменяет другой писатель, и ограниченный повтор снимает лишь часть таких отказов.
    /// Гарантируется другое: отдельный отказ выходит структурным <c>config_error</c>,
    /// хотя бы одна запись доходит до диска, итоговый файл читается целиком и согласован,
    /// временных файлов не остаётся.
    /// </remarks>
    [Test]
    public async Task SaveAsync_ConcurrentWrites_NeverProduceTruncatedFile()
    {
        var dir = CreateTempDir();
        var path = Path.Combine(dir, "config.json");

        // Профилей много, чтобы запись не укладывалась в одну мгновенную операцию.
        static ConfigFile Big(string marker) => new(
            marker,
            Enumerable.Range(0, 200).ToDictionary(i => $"p{i}", i => Oauth($"{marker}-{i}")));

        var tasks = Enumerable.Range(0, 16).Select(i => Task.Run(async () =>
        {
            try
            {
                await new ConfigStore(path).SaveAsync(Big($"m{i}"));
                return true;
            }
            catch (TrackerException ex) when (ex.Code == ErrorCode.ConfigError)
            {
                // Конкурентная запись вправе отказать — но только предъявив структурную ошибку.
                return false;
            }
        }));
        var succeeded = await Task.WhenAll(tasks);

        // Хотя бы одна запись обязана дойти до диска.
        await Assert.That(succeeded.Any(ok => ok)).IsTrue();

        // Файл читается целиком и остаётся валидным JSON — усечения не случилось.
        var cfg = await new ConfigStore(path).LoadAsync();
        await Assert.That(cfg.Profiles).Count().IsEqualTo(200);

        // Содержимое принадлежит ровно одному писателю: маркер согласован по всем профилям.
        var marker = cfg.DefaultProfile;
        for (var i = 0; i < 200; i++)
        {
            await Assert.That(cfg.Profiles[$"p{i}"].Auth.Token).IsEqualTo($"{marker}-{i}");
        }

        // Временные файлы за собой не оставляем.
        await Assert.That(Directory.GetFiles(dir, "*.tmp")).IsEmpty();
    }

    [Test]
    public async Task ModifyAsync_WhenLockIsHeld_FailsWithConfigError_InsteadOfHanging()
    {
        var path = Path.Combine(CreateTempDir(), "config.json");
        var store = new ConfigStore(path, lockTimeout: TimeSpan.FromMilliseconds(200));
        await store.SaveAsync(new ConfigFile("a", new Dictionary<string, Profile>()));

        // Держим лок так же, как это делает другой процесс.
        using var holder = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        TrackerException? caught = null;
        var started = DateTime.UtcNow;
        try
        {
            await store.ModifyAsync(fresh => fresh);
        }
        catch (TrackerException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Code).IsEqualTo(ErrorCode.ConfigError);
        await Assert.That(caught.Message).Contains("lock");
        // Ожидание ограничено таймаутом, а не бесконечно.
        await Assert.That(DateTime.UtcNow - started).IsLessThan(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task ModifyAsync_LockFile_IsOwnerOnly_OnUnix()
    {
        var path = Path.Combine(CreateTempDir(), "config.json");
        var store = new ConfigStore(path);
        await store.ModifyAsync(fresh => fresh);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var mode = File.GetUnixFileMode(path + ".lock");
            await Assert.That(mode & UnixFileMode.GroupRead).IsEqualTo((UnixFileMode)0);
            await Assert.That(mode & UnixFileMode.OtherRead).IsEqualTo((UnixFileMode)0);
        }
    }

    [Test]
    public async Task ModifyAsync_WhenMutationThrows_LeavesFileAndLockUsable()
    {
        var dir = CreateTempDir();
        var path = Path.Combine(dir, "config.json");
        var store = new ConfigStore(path);
        await store.SaveAsync(new ConfigFile("a", new() { ["a"] = Oauth("token-a") }));

        try
        {
            await store.ModifyAsync(_ => throw new InvalidOperationException("boom"));
        }
        catch (InvalidOperationException)
        {
            /* expected */
        }

        // Лок отпущен: следующая правка проходит, файл не тронут неудачной попыткой.
        await store.ModifyAsync(fresh => new ConfigFile("b", fresh.Profiles));
        var cfg = await store.LoadAsync();
        await Assert.That(cfg.DefaultProfile).IsEqualTo("b");
        await Assert.That(cfg.Profiles["a"].Auth.Token).IsEqualTo("token-a");
        await Assert.That(Directory.GetFiles(dir, "*.tmp")).IsEmpty();
    }

    /// <summary>
    /// Неудачная запись не должна оставлять временный файл рядом с конфигом: раньше он
    /// накапливался бы под одним именем, теперь имена уникальны — и мусор копился бы тем быстрее.
    /// Отказ при этом обязан выйти как <see cref="ErrorCode.ConfigError"/>: команды ловят
    /// только <see cref="TrackerException"/>, сырой <see cref="IOException"/> дошёл бы до
    /// пользователя стектрейсом.
    /// </summary>
    [Test]
    public async Task SaveAsync_WhenCommitFails_RemovesTempFile()
    {
        var dir = CreateTempDir();
        var path = Path.Combine(dir, "config.json");

        // Целевой путь занят каталогом — переименование поверх него провалится.
        Directory.CreateDirectory(path);
        var store = new ConfigStore(path);

        var ex = await Assert.ThrowsAsync<TrackerException>(
            async () => await store.SaveAsync(new ConfigFile("a", new() { ["a"] = Oauth("t") })));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.ConfigError);
        await Assert.That(Directory.GetFiles(dir, "*.tmp")).IsEmpty();
    }

    /// <summary>
    /// Таймаут задаётся на вызов, а не только на весь store: путь после интерактивного
    /// входа держит только что выданные креденшелы в памяти и обязан переждать чужую
    /// запись, а не выбросить их по общему десятисекундному дефолту.
    /// </summary>
    [Test]
    public async Task ModifyAsync_PerCallTimeout_OutwaitsHolder_BeyondStoreDefault()
    {
        var path = Path.Combine(CreateTempDir(), "config.json");
        var store = new ConfigStore(path, lockTimeout: TimeSpan.FromMilliseconds(50));
        await store.SaveAsync(new ConfigFile("a", new Dictionary<string, Profile>()));

        var holder = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(400);
            holder.Dispose();
        });

        var written = await store.ModifyAsync(
            fresh => new ConfigFile(fresh.DefaultProfile, new Dictionary<string, Profile>(fresh.Profiles)
            {
                ["late"] = Oauth("token-late"),
            }),
            lockTimeout: TimeSpan.FromSeconds(30));

        await release;
        await Assert.That(written.Profiles.ContainsKey("late")).IsTrue();
        var cfg = await store.LoadAsync();
        await Assert.That(cfg.Profiles["late"].Auth.Token).IsEqualTo("token-late");
    }

    /// <summary>
    /// Значение, посчитанное по свежему снимку (например, разрешённое имя профиля),
    /// возвращается результатом, а не утекает через захваченную локальную.
    /// </summary>
    [Test]
    public async Task ModifyAsync_ReturnsValueComputedFromFreshSnapshot()
    {
        var path = Path.Combine(CreateTempDir(), "config.json");
        var store = new ConfigStore(path);
        await store.SaveAsync(new ConfigFile("a", new() { ["a"] = Oauth("token-a") }));

        // Сторонняя правка ложится на диск после того, как «команда» уже стартовала.
        await store.SaveAsync(new ConfigFile("b", new()
        {
            ["a"] = Oauth("token-a"),
            ["b"] = Oauth("token-b"),
        }));

        var update = await store.ModifyAsync(fresh =>
        {
            var target = fresh.DefaultProfile;
            var profiles = new Dictionary<string, Profile>(fresh.Profiles) { [target] = Oauth("cleared") };
            return new ConfigUpdate<string>(new ConfigFile(fresh.DefaultProfile, profiles), target);
        });

        await Assert.That(update.Result).IsEqualTo("b");
        await Assert.That(update.Config.Profiles["b"].Auth.Token).IsEqualTo("cleared");
    }

    /// <summary>
    /// Каталог конфига только для чтения: наружу это обязано выйти структурной ошибкой
    /// <c>config_error</c>, а не необработанным <see cref="UnauthorizedAccessException"/> —
    /// команды ловят только <see cref="TrackerException"/>.
    /// </summary>
    [Test]
    public async Task ModifyAsync_WhenDirectoryIsReadOnly_FailsWithConfigError()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return; // Права на каталог здесь моделируются POSIX-режимом.
        }

        var dir = CreateTempDir();
        var path = Path.Combine(dir, "config.json");
        var store = new ConfigStore(path, lockTimeout: TimeSpan.FromMilliseconds(200));
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            if (CanStillWrite(dir))
            {
                return; // root игнорирует биты прав — проверять нечего.
            }

            TrackerException? caught = null;
            try
            {
                await store.ModifyAsync(fresh => fresh);
            }
            catch (TrackerException ex)
            {
                caught = ex;
            }

            await Assert.That(caught).IsNotNull();
            await Assert.That(caught!.Code).IsEqualTo(ErrorCode.ConfigError);
        }
        finally
        {
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// Проверяет, что биты прав каталога действительно запрещают запись текущему процессу
    /// (под root они не запрещают ничего).
    /// </summary>
    private static bool CanStillWrite(string dir)
    {
        var probe = Path.Combine(dir, "probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static Profile Oauth(string token) =>
        new(OrgType.Cloud, "org-1", false, new AuthConfig(AuthType.OAuth, Token: token));

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "yt-cli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
