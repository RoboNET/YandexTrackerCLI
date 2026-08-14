namespace YandexTrackerCLI.Core.Auth;

using System.Text.Json;
using System.Text.Json.Serialization;
using Json;
using Storage;

/// <summary>
/// Represents a cached IAM token entry with its expiration timestamp.
/// </summary>
/// <param name="Token">The IAM token value.</param>
/// <param name="ExpiresAt">The UTC instant at which the token expires.</param>
public sealed record TokenCacheEntry(
    [property: JsonPropertyName("token")]      string Token,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);

/// <summary>
/// File-backed cache for IAM tokens keyed by a caller-supplied string identifier.
/// Entries are serialized as JSON and persisted with user-only permissions on Unix.
/// Expired entries (within a 60-second leeway) are treated as absent on read.
/// </summary>
public sealed class TokenCache
{
    private static readonly TimeSpan Leeway = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Default time <see cref="SetAsync"/> waits for the cross-process lock before failing.
    /// </summary>
    public static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);

    private readonly string _path;
    private readonly TimeSpan _lockTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new <see cref="TokenCache"/> backed by the specified file path.
    /// </summary>
    /// <param name="path">Absolute path to the cache file.</param>
    /// <param name="lockTimeout">
    /// How long <see cref="SetAsync"/> waits for the update lock;
    /// <c>null</c> selects <see cref="DefaultLockTimeout"/>.
    /// </param>
    public TokenCache(string path, TimeSpan? lockTimeout = null)
    {
        _path = path;
        _lockTimeout = lockTimeout ?? DefaultLockTimeout;
    }

    private string LockPath => _path + ".lock";

    /// <summary>
    /// Gets the default cache file path, honoring <c>XDG_CACHE_HOME</c> on Unix
    /// and falling back to <c>$HOME/.cache/yandex-tracker/iam-tokens.json</c>.
    /// </summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
                ?? Path.Combine(PathResolver.ResolveHome(), ".cache"),
            "yandex-tracker",
            "iam-tokens.json");

    /// <summary>
    /// Retrieves a cached entry by key if it exists and has not expired (accounting for the leeway).
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="evaluatedAt">Optional override for the current time (for deterministic tests).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cached <see cref="TokenCacheEntry"/>, or <c>null</c> if missing or (near-)expired.</returns>
    public async Task<TokenCacheEntry?> GetAsync(string key, DateTimeOffset? evaluatedAt = null, CancellationToken ct = default)
    {
        var now = evaluatedAt ?? DateTimeOffset.UtcNow;
        var all = await LoadAsync(ct);
        if (!all.TryGetValue(key, out var entry)) return null;
        return entry.ExpiresAt - now > Leeway ? entry : null;
    }

    /// <summary>
    /// Stores a token under the given key with the specified expiration.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="token">The IAM token value.</param>
    /// <param name="expiresAt">The UTC expiration timestamp.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="Api.Errors.TrackerException">
    /// <see cref="Api.Errors.ErrorCode.ConfigError"/> — the update lock could not be acquired in time.
    /// </exception>
    /// <remarks>
    /// The read-modify-write is serialized twice: an in-process semaphore, and an advisory
    /// file lock (<c>iam-tokens.json.lock</c>) that also covers concurrent <c>yt</c> processes —
    /// token refreshes are frequent, so two of them landing at once is not a theoretical case.
    /// The commit itself is atomic (unique temp file + rename), so a losing writer can only
    /// lose its entry, never truncate someone else's file.
    /// </remarks>
    public async Task SetAsync(string key, string token, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var lockHandle = await FileStore.AcquireLock(LockPath, _lockTimeout, ct);

            var all = await LoadAsync(ct);
            all[key] = new TokenCacheEntry(token, expiresAt);
            await SaveAsync(all, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, TokenCacheEntry>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, TokenCacheEntry>();
        }
        await using var fs = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync(fs, TrackerJsonContext.Default.DictionaryStringTokenCacheEntry, ct)
            ?? new Dictionary<string, TokenCacheEntry>();
    }

    private Task SaveAsync(Dictionary<string, TokenCacheEntry> all, CancellationToken ct) =>
        FileStore.WriteAtomic(
            _path,
            (stream, token) => JsonSerializer.SerializeAsync(
                stream,
                all,
                TrackerJsonContext.Default.DictionaryStringTokenCacheEntry,
                token),
            ct);
}
