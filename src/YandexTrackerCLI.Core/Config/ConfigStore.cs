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

    /// <summary>
    /// Lock timeout for writes that happen <b>after</b> an interactive authentication:
    /// <c>auth login</c> and the federated re-login.
    /// </summary>
    /// <remarks>
    /// Much longer than <see cref="DefaultLockTimeout"/> on purpose. By this point the browser
    /// flow has already run for minutes and the server has already rotated the tokens: the old
    /// refresh token is dead server-side and the new session exists only in memory. Giving up
    /// after ten seconds would throw away credentials that cannot be re-derived and leave on
    /// disk the very broken ones that triggered the re-login. Waiting a minute for another
    /// <c>yt</c> to finish its sub-second write is the cheaper failure.
    /// </remarks>
    public static readonly TimeSpan PostAuthLockTimeout = TimeSpan.FromSeconds(60);

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

        // Открываем с FileShare.Delete: иначе на Windows конкурентная запись (rename поверх
        // этого файла) падала бы нарушением совместного доступа — то есть читатель ронял бы
        // чужое сохранение.
        await using var fs = FileStore.OpenSharedRead(_path);
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
    public Task<ConfigFile> ModifyAsync(Func<ConfigFile, ConfigFile> mutate, CancellationToken ct = default) =>
        ModifyAsync(mutate, lockTimeout: null, ct);

    /// <summary>
    /// Same as <see cref="ModifyAsync(Func{ConfigFile, ConfigFile}, CancellationToken)"/>, with
    /// an explicit lock timeout for this call.
    /// </summary>
    /// <param name="mutate">Transformation applied to the fresh on-disk configuration.</param>
    /// <param name="lockTimeout">
    /// How long to wait for the lock; <c>null</c> selects the store-wide timeout.
    /// Callers that reach this point holding credentials that exist nowhere else — anything
    /// past an interactive login — should pass a generous value: failing here discards a
    /// session that cost the user a browser round-trip.
    /// </param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The configuration that was written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mutate"/> is <c>null</c>.</exception>
    /// <exception cref="Api.Errors.TrackerException">
    /// <see cref="Api.Errors.ErrorCode.ConfigError"/> — the lock could not be acquired in time.
    /// </exception>
    public async Task<ConfigFile> ModifyAsync(
        Func<ConfigFile, ConfigFile> mutate,
        TimeSpan? lockTimeout,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        var update = await ModifyAsync(
            fresh => new ConfigUpdate<bool>(
                mutate(fresh) ?? throw new ArgumentException("The mutate callback returned null.", nameof(mutate)),
                true),
            lockTimeout,
            ct);
        return update.Config;
    }

    /// <summary>
    /// Read-modify-write that also hands back a value computed from the fresh snapshot —
    /// the resolved profile name, for instance.
    /// </summary>
    /// <typeparam name="TResult">Type of the value produced alongside the new configuration.</typeparam>
    /// <param name="mutate">
    /// Transformation from the on-disk configuration to the one to persist, paired with the
    /// value to return.
    /// <para>
    /// The signature is deliberately <b>synchronous</b> (<c>Func&lt;…&gt;</c>, not
    /// <c>Func&lt;…, Task&lt;…&gt;&gt;</c>), and that is not an oversight to be "generalized"
    /// away. It is what structurally rules out awaiting anything inside the lock — most of all
    /// a nested <see cref="ModifyAsync{TResult}"/>, which would try to take a lock this call
    /// already holds and simply sit there until the timeout, on the same thread that is the
    /// only one able to release it. An async callback would also invite the browser flow or an
    /// HTTP call back under the lock, where it blocks every other <c>yt</c> invocation for its
    /// whole duration.
    /// </para>
    /// </param>
    /// <param name="lockTimeout">
    /// How long to wait for the lock; <c>null</c> selects the store-wide timeout.
    /// </param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The value produced by <paramref name="mutate"/>, next to the written configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mutate"/> is <c>null</c>.</exception>
    /// <exception cref="Api.Errors.TrackerException">
    /// <see cref="Api.Errors.ErrorCode.ConfigError"/> — the lock could not be acquired in time.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The lock is advisory (on Unix .NET implements <see cref="FileShare"/> via <c>flock(2)</c>),
    /// so it serializes <c>yt</c> against itself, not against an unrelated process editing the
    /// file. That is the whole requirement here: nothing else writes this file.
    /// </para>
    /// <para>
    /// On a network filesystem the exclusivity can degrade silently — on SMB/CIFS and in some
    /// NFS setups both processes acquire the "exclusive" handle. Lost updates come back in that
    /// case; a corrupted file does not, because the commit is still a unique temp file plus a
    /// rename.
    /// </para>
    /// </remarks>
    public async Task<ConfigUpdate<TResult>> ModifyAsync<TResult>(
        Func<ConfigFile, ConfigUpdate<TResult>> mutate,
        TimeSpan? lockTimeout = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        await using var lockHandle = await FileStore.AcquireLock(LockPath, lockTimeout ?? _lockTimeout, ct);

        var current = await LoadAsync(ct);
        var update = mutate(current);
        if (update.Config is null)
        {
            throw new ArgumentException("The mutate callback returned a null configuration.", nameof(mutate));
        }

        await SaveAsync(update.Config, ct);
        return update;
    }
}

/// <summary>
/// Pair returned by <see cref="ConfigStore.ModifyAsync{TResult}"/>: the configuration to
/// persist and a value derived from the same fresh snapshot.
/// </summary>
/// <typeparam name="TResult">Type of the derived value.</typeparam>
/// <param name="Config">The configuration to write.</param>
/// <param name="Result">The value to hand back to the caller.</param>
/// <remarks>
/// Exists so callers stop smuggling such values out through a captured local: that only works
/// while the callback runs exactly once and is never retried, and nothing in the signature
/// says so.
/// </remarks>
public readonly record struct ConfigUpdate<TResult>(ConfigFile Config, TResult Result);
