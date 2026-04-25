namespace YandexTrackerCLI.Core.Auth;

using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Json;

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

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new <see cref="TokenCache"/> backed by the specified file path.
    /// </summary>
    /// <param name="path">Absolute path to the cache file.</param>
    public TokenCache(string path) => _path = path;

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
    /// Writes are serialized via an in-process semaphore and committed atomically via <c>.tmp</c> + rename.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="token">The IAM token value.</param>
    /// <param name="expiresAt">The UTC expiration timestamp.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SetAsync(string key, string token, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
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

    private async Task SaveAsync(Dictionary<string, TokenCacheEntry> all, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = _path + ".tmp";
        await using (var fs = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(fs, all, TrackerJsonContext.Default.DictionaryStringTokenCacheEntry, ct);
        }
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        File.Move(tmp, _path, overwrite: true);
    }
}
