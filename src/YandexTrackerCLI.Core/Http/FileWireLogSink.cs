namespace YandexTrackerCLI.Core.Http;

using System.Text;
using Api.Errors;

/// <summary>
/// Append-only file-backed implementation of <see cref="IWireLogSink"/> that serializes
/// concurrent writes through a <see cref="SemaphoreSlim"/>.
/// </summary>
/// <remarks>
/// On POSIX systems the underlying file is created with mode <c>0600</c> on first use.
/// The path supports a leading <c>~/</c> which is expanded to the current user's home
/// directory. Missing parent directories are created automatically. All writes use
/// UTF-8 encoding without BOM and emit <c>\n</c> line endings unchanged from the
/// caller-provided text.
/// </remarks>
public sealed class FileWireLogSink : IWireLogSink
{
    private readonly FileStream _stream;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    private FileWireLogSink(FileStream stream) => _stream = stream;

    /// <summary>
    /// Resolves <paramref name="path"/> (expanding a leading <c>~/</c>), creates any
    /// missing parent directories, and opens the destination file in append mode.
    /// </summary>
    /// <param name="path">Target file path; may begin with <c>~/</c>.</param>
    /// <returns>A new <see cref="FileWireLogSink"/> ready to receive writes.</returns>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.ConfigError"/> — when the path is empty or the file cannot be opened.
    /// </exception>
    public static FileWireLogSink Create(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new TrackerException(ErrorCode.ConfigError, "Wire-log path is empty.");
        }

        var resolved = Expand(path);
        try
        {
            var dir = Path.GetDirectoryName(resolved);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var existed = File.Exists(resolved);
            var fs = new FileStream(
                resolved,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            if (!existed && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
            {
                try
                {
                    File.SetUnixFileMode(
                        resolved,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch (Exception ex)
                {
                    fs.Dispose();
                    throw new TrackerException(
                        ErrorCode.ConfigError,
                        $"Failed to chmod 600 on wire-log file '{resolved}': {ex.Message}",
                        inner: ex);
                }
            }

            return new FileWireLogSink(fs);
        }
        catch (TrackerException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TrackerException(
                ErrorCode.ConfigError,
                $"Failed to open wire-log file '{resolved}': {ex.Message}",
                inner: ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(string text, CancellationToken ct)
    {
        if (_disposed)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        await _gate.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(bytes, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _gate.WaitAsync();
        try
        {
            await _stream.FlushAsync();
            await _stream.DisposeAsync();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private static string Expand(string path)
    {
        if (path.Length >= 2 && path[0] == '~' && (path[1] == '/' || path[1] == Path.DirectorySeparatorChar))
        {
            var home = PathResolver.ResolveHome();
            return Path.Combine(home, path[2..]);
        }

        if (path == "~")
        {
            return PathResolver.ResolveHome();
        }

        return path;
    }
}
