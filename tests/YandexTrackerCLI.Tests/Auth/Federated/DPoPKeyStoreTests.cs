namespace YandexTrackerCLI.Tests.Auth.Federated;

using System.Runtime.InteropServices;
using TUnit.Core;
using YandexTrackerCLI.Auth.Federated;

/// <summary>
/// Юнит-тесты <see cref="DPoPKeyStore"/>: генерация, загрузка, UNIX-permissions.
/// </summary>
public sealed class DPoPKeyStoreTests
{
    [Test]
    public async Task LoadOrCreate_NoFile_GeneratesAndPersists()
    {
        var path = Path.Combine(Path.GetTempPath(), "yt-dpop-" + Guid.NewGuid().ToString("N") + ".pem");
        try
        {
            var store = new DPoPKeyStore(path);
            using var key = store.LoadOrCreate();

            await Assert.That(File.Exists(path)).IsTrue();
            await Assert.That(key.KeySize).IsEqualTo(256);
            var parameters = key.ExportParameters(includePrivateParameters: false);
            await Assert.That(parameters.Q.X).IsNotNull();
            await Assert.That(parameters.Q.Y).IsNotNull();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public async Task LoadOrCreate_ExistingFile_ReturnsSameKeyMaterial()
    {
        var path = Path.Combine(Path.GetTempPath(), "yt-dpop-" + Guid.NewGuid().ToString("N") + ".pem");
        try
        {
            var store = new DPoPKeyStore(path);
            using var first = store.LoadOrCreate();
            var firstX = first.ExportParameters(false).Q.X!;

            using var second = new DPoPKeyStore(path).LoadOrCreate();
            var secondX = second.ExportParameters(false).Q.X!;

            await Assert.That(secondX).IsEquivalentTo(firstX);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public async Task LoadOrCreate_OnUnix_SetsOwnerOnlyMode()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // No POSIX mode bits on Windows; test is effectively a no-op.
            // Record a single trivial assertion so test runners still count a result.
            var platform = RuntimeInformation.OSDescription;
            await Assert.That(platform).IsNotNull();
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), "yt-dpop-" + Guid.NewGuid().ToString("N") + ".pem");
        try
        {
            var store = new DPoPKeyStore(path);
            using var key = store.LoadOrCreate();

            var mode = File.GetUnixFileMode(path);
            var masked = mode & (UnixFileMode.GroupRead
                                 | UnixFileMode.GroupWrite
                                 | UnixFileMode.GroupExecute
                                 | UnixFileMode.OtherRead
                                 | UnixFileMode.OtherWrite
                                 | UnixFileMode.OtherExecute);
            await Assert.That(masked).IsEqualTo(UnixFileMode.None);
            await Assert.That(mode.HasFlag(UnixFileMode.UserRead)).IsTrue();
            await Assert.That(mode.HasFlag(UnixFileMode.UserWrite)).IsTrue();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
