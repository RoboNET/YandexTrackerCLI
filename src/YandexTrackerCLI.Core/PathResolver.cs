namespace YandexTrackerCLI.Core;

/// <summary>
/// Path-resolution helpers shared by config, cache, and skill installers.
/// </summary>
/// <remarks>
/// Centralised here so the test harness can override the user-home location uniformly
/// across both <see cref="YandexTrackerCLI.Core"/> and the CLI assembly. The default
/// <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> implementation reads
/// <c>HOME</c> on POSIX but goes to the registry/Win32 SHGetKnownFolderPath on Windows,
/// which makes <c>USERPROFILE</c> overrides invisible to test fixtures running under CI.
/// </remarks>
public static class PathResolver
{
    /// <summary>
    /// Resolves the current user's home directory, honouring <c>HOME</c> and <c>USERPROFILE</c>
    /// environment variables before falling back to the platform's
    /// <see cref="Environment.SpecialFolder.UserProfile"/> lookup.
    /// </summary>
    /// <returns>An absolute path to the user's home directory. Never <c>null</c>; in pathological
    /// configurations may be an empty string if the platform reports no profile.</returns>
    /// <remarks>
    /// Resolution order:
    /// <list type="number">
    ///   <item><description><c>HOME</c> (canonical on Linux/macOS; honoured on Windows when set).</description></item>
    ///   <item><description><c>USERPROFILE</c> (canonical on Windows; honoured on POSIX when set).</description></item>
    ///   <item><description><see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/>
    ///     with <see cref="Environment.SpecialFolder.UserProfile"/>.</description></item>
    /// </list>
    /// In production both <c>HOME</c> (POSIX) and <c>USERPROFILE</c> (Windows) are reliably set
    /// by the OS, so behaviour is identical to the previous <c>GetFolderPath</c> call. In test
    /// fixtures the env-var path lets a sandbox redirect every reader at once.
    /// </remarks>
    public static string ResolveHome()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home))
        {
            return home;
        }

        var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrEmpty(userProfile))
        {
            return userProfile;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
