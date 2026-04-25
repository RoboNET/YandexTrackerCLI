namespace YandexTrackerCLI.Core.Config;

using System.Runtime.InteropServices;
using System.Text.Json;
using Json;

/// <summary>
/// Loads and saves the Yandex Tracker CLI configuration file with atomic writes
/// and POSIX permissions restricted to the owner (0600) on non-Windows systems.
/// </summary>
public sealed class ConfigStore
{
    private readonly string _path;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigStore"/> class.
    /// </summary>
    /// <param name="path">Absolute path to the configuration JSON file.</param>
    public ConfigStore(string path)
    {
        _path = path;
    }

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
    /// Saves the configuration to disk using an atomic write (temp file plus rename),
    /// creating missing parent directories and setting file permissions to owner-only (0600) on POSIX.
    /// </summary>
    /// <param name="cfg">The configuration to persist.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task SaveAsync(ConfigFile cfg, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = _path + ".tmp";
        await using (var fs = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(fs, cfg, TrackerJsonContext.Default.ConfigFile, ct);
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(tmp, _path, overwrite: true);
    }
}
