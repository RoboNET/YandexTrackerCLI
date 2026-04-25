namespace YandexTrackerCLI.Core.Tests.Config;

using System.Runtime.InteropServices;
using TUnit.Core;
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

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "yt-cli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
