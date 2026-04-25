namespace YandexTrackerCLI.Core.Tests.Http;

using System.Text;
using TUnit.Core;
using YandexTrackerCLI.Core.Api.Errors;
using YandexTrackerCLI.Core.Http;

public sealed class FileWireLogSinkTests
{
    [Test]
    public async Task Create_RejectsEmptyPath()
    {
        var ex = Assert.Throws<TrackerException>(() => FileWireLogSink.Create(""));
        await Assert.That(ex.Code).IsEqualTo(ErrorCode.ConfigError);
    }

    [Test]
    public async Task Create_CreatesParentDirectory_IfMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "yt-wire-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "subdir", "log.txt");
        try
        {
            await using (var sink = FileWireLogSink.Create(path))
            {
                await sink.WriteAsync("hello\n", CancellationToken.None);
            }

            await Assert.That(File.Exists(path)).IsTrue();
            var bytes = await File.ReadAllBytesAsync(path);
            await Assert.That(Encoding.UTF8.GetString(bytes)).IsEqualTo("hello\n");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Test]
    public async Task Create_AppendsToExistingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "yt-wire-append-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            await using (var sink = FileWireLogSink.Create(path))
            {
                await sink.WriteAsync("first\n", CancellationToken.None);
            }
            await using (var sink = FileWireLogSink.Create(path))
            {
                await sink.WriteAsync("second\n", CancellationToken.None);
            }

            var text = await File.ReadAllTextAsync(path);
            await Assert.That(text).IsEqualTo("first\nsecond\n");
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
    public async Task Create_SetsChmod600_OnPosix()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
        {
            return; // Windows: ACL-based, no UnixFileMode.
        }

        var path = Path.Combine(Path.GetTempPath(), "yt-wire-chmod-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            await using (var sink = FileWireLogSink.Create(path))
            {
                await sink.WriteAsync("x", CancellationToken.None);
            }

            var mode = File.GetUnixFileMode(path);
            var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await Assert.That(mode).IsEqualTo(expected);
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
    public async Task Create_ExpandsTildePrefix()
    {
        // Use a unique temp file under the home directory so we can verify ~ expansion
        // resolves to $HOME/<name>.
        var name = "yt-wire-tilde-" + Guid.NewGuid().ToString("N") + ".log";
        var tildePath = "~/" + name;
        var expectedAbs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), name);
        try
        {
            await using (var sink = FileWireLogSink.Create(tildePath))
            {
                await sink.WriteAsync("home\n", CancellationToken.None);
            }

            await Assert.That(File.Exists(expectedAbs)).IsTrue();
            var text = await File.ReadAllTextAsync(expectedAbs);
            await Assert.That(text).IsEqualTo("home\n");
        }
        finally
        {
            if (File.Exists(expectedAbs))
            {
                File.Delete(expectedAbs);
            }
        }
    }

    [Test]
    public async Task ConcurrentWrites_NoCorruption()
    {
        var path = Path.Combine(Path.GetTempPath(), "yt-wire-concurrent-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            await using (var sink = FileWireLogSink.Create(path))
            {
                var tasks = new Task[10];
                for (var i = 0; i < tasks.Length; i++)
                {
                    var idx = i;
                    var line = "line-" + idx + new string('x', 64) + "\n";
                    tasks[i] = Task.Run(async () =>
                    {
                        for (var k = 0; k < 5; k++)
                        {
                            await sink.WriteAsync(line, CancellationToken.None);
                        }
                    });
                }

                await Task.WhenAll(tasks);
            }

            var text = await File.ReadAllTextAsync(path);
            // Expect exactly 10*5 lines, each starting with "line-<i>" followed by 64 'x'.
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            await Assert.That(lines.Length).IsEqualTo(50);
            foreach (var line in lines)
            {
                await Assert.That(line.StartsWith("line-", StringComparison.Ordinal)).IsTrue();
                // Verify no interleaving: each line is exactly the original "line-<i>" + 64 x's
                // Original length: "line-" (5) + at most 2 digit + 64 = 70-71.
                await Assert.That(line.Length >= 5 + 1 + 64).IsTrue();
                await Assert.That(line.Length <= 5 + 2 + 64).IsTrue();
            }
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
