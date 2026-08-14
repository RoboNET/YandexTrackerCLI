namespace YandexTrackerCLI.Core.Config;

using System.Text.Json;
using Json;
using Storage;

/// <summary>
/// Loads and saves the Yandex Tracker CLI configuration file with atomic writes
/// and POSIX permissions restricted to the owner (0600) on non-Windows systems.
/// </summary>
/// <remarks>
/// Read-modify-write updates must go through <see cref="ModifyAsync"/>: a bare
/// <see cref="LoadAsync"/> + <see cref="SaveAsync"/> pair silently reverts anything another
/// <c>yt</c> process wrote to the file in between, including a tightened profile policy.
/// </remarks>
public sealed class ConfigStore
{
    /// <summary>
    /// Default time <see cref="ModifyAsync"/> waits for the cross-process lock before failing.
    /// Bounded on purpose: a hung holder must surface as an error, not as a CLI that never returns.
    /// </summary>
    public static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);

    private readonly string _path;
    private readonly TimeSpan _lockTimeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigStore"/> class.
    /// </summary>
    /// <param name="path">Absolute path to the configuration JSON file.</param>
    /// <param name="lockTimeout">
    /// How long <see cref="ModifyAsync"/> waits for the update lock;
    /// <c>null</c> selects <see cref="DefaultLockTimeout"/>.
    /// </param>
    public ConfigStore(string path, TimeSpan? lockTimeout = null)
    {
        _path = path;
        _lockTimeout = lockTimeout ?? DefaultLockTimeout;
    }

    /// <summary>
    /// Path of the advisory lock file guarding updates (<c>config.json.lock</c>).
    /// </summary>
    /// <remarks>
    /// A separate file, not the config itself: <see cref="SaveAsync"/> commits via rename,
    /// so a lock held on the pre-rename inode would guard nothing.
    /// </remarks>
    private string LockPath => _path + ".lock";

    /// <summary>
    /// Gets the default configuration file path, honoring <c>YT_CONFIG_PATH</c>
    /// and <c>XDG_CONFIG_HOME</c> environment variables.
    /// </summary>
    public static string DefaultPath =>
        Environment.GetEnvironmentVariable("YT_CONFIG_PATH")
        ?? Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                ?? Path.Combine(PathResolver.ResolveHome(), ".config"),
            "yandex-tracker",
            "config.json");

    /// <summary>
    /// Loads the configuration from disk. Returns an empty configuration if the file does not exist.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The deserialized <see cref="ConfigFile"/>.</returns>
    public async Task<ConfigFile> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path))
        {
            return new ConfigFile("default", new Dictionary<string, Profile>());
        }

        await using var fs = File.OpenRead(_path);
        var cfg = await JsonSerializer.DeserializeAsync(fs, TrackerJsonContext.Default.ConfigFile, ct);
        return cfg ?? new ConfigFile("default", new Dictionary<string, Profile>());
    }

    /// <summary>
    /// Saves the configuration to disk using an atomic write (unique temp file plus rename),
    /// creating missing parent directories and setting file permissions to owner-only (0600) on POSIX.
    /// </summary>
    /// <param name="cfg">The configuration to persist.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <remarks>
    /// Overwrites the file wholesale and takes no lock. Use it only when the content does not
    /// depend on what is currently on disk; for updates derived from the existing configuration
    /// use <see cref="ModifyAsync"/>.
    /// </remarks>
    public Task SaveAsync(ConfigFile cfg, CancellationToken ct = default) =>
        FileStore.WriteAtomic(
            _path,
            (stream, token) => JsonSerializer.SerializeAsync(stream, cfg, TrackerJsonContext.Default.ConfigFile, token),
            ct);

    /// <summary>
    /// Atomically applies an update to the configuration: takes the cross-process lock,
    /// re-reads the file from disk <b>inside</b> the lock, applies <paramref name="mutate"/>
    /// to that fresh snapshot, writes the result and releases the lock.
    /// </summary>
    /// <param name="mutate">
    /// Transformation from the on-disk configuration to the one to persist. It runs while the
    /// lock is held, so it must be quick and must not perform interactive or network work —
    /// the browser login flow, for example, has to complete <em>before</em> this call.
    /// </param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The configuration that was written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mutate"/> is <c>null</c>.</exception>
    /// <exception cref="Api.Errors.TrackerException">
    /// <see cref="Api.Errors.ErrorCode.ConfigError"/> — the lock could not be acquired in time.
    /// </exception>
    /// <remarks>
    /// The lock is advisory (on Unix .NET implements <see cref="FileShare"/> via <c>flock(2)</c>),
    /// so it serializes <c>yt</c> against itself, not against an unrelated process editing the
    /// file. That is the whole requirement here: nothing else writes this file.
    /// </remarks>
    public async Task<ConfigFile> ModifyAsync(Func<ConfigFile, ConfigFile> mutate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        await using var lockHandle = await FileStore.AcquireLock(LockPath, _lockTimeout, ct);

        var current = await LoadAsync(ct);
        var updated = mutate(current)
            ?? throw new ArgumentException("The mutate callback returned null.", nameof(mutate));
        await SaveAsync(updated, ct);
        return updated;
    }
}
