namespace YandexTrackerCLI.Auth.Federated;

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using YandexTrackerCLI.Core;

/// <summary>
/// File-backed store for a per-profile ECDSA P-256 key pair used to sign DPoP
/// (Demonstrating Proof-of-Possession, RFC 9449) JWTs.
/// </summary>
/// <remarks>
/// The key is persisted as PKCS#8 PEM in a user-owned file (mode 0600 on Unix).
/// The same private key must be used for the lifetime of a federated profile —
/// the token server binds a JWK thumbprint of its public component to both the
/// access token and the refresh token.
/// </remarks>
public sealed class DPoPKeyStore
{
    private readonly string _path;

    /// <summary>
    /// Initializes a new <see cref="DPoPKeyStore"/> backed by the file at
    /// <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Absolute path to the PEM-encoded private key file.</param>
    public DPoPKeyStore(string path) => _path = path;

    /// <summary>
    /// Gets the path this store persists to.
    /// </summary>
    public string Path => _path;

    /// <summary>
    /// Builds the default on-disk location for the federated DPoP key of a
    /// given profile, honoring <c>XDG_CONFIG_HOME</c> on Unix.
    /// </summary>
    /// <param name="profileName">The configuration profile name.</param>
    /// <returns>Absolute path that does not yet need to exist.</returns>
    public static string DefaultPathForProfile(string profileName) =>
        System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                ?? System.IO.Path.Combine(
                    PathResolver.ResolveHome(),
                    ".config"),
            "yandex-tracker",
            "federated-keys",
            $"{profileName}.pem");

    /// <summary>
    /// Loads an existing key from disk if one is present; otherwise generates a fresh
    /// ECDSA P-256 key pair, writes it (PKCS#8 PEM) with owner-only permissions, and
    /// returns it.
    /// </summary>
    /// <returns>The loaded or freshly generated <see cref="ECDsa"/>. Owned by the caller.</returns>
    public ECDsa LoadOrCreate()
    {
        if (File.Exists(_path))
        {
            var pem = File.ReadAllText(_path);
            var ecdsa = ECDsa.Create();
            try
            {
                ecdsa.ImportFromPem(pem);
                return ecdsa;
            }
            catch
            {
                ecdsa.Dispose();
                throw;
            }
        }

        var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            var export = newKey.ExportPkcs8PrivateKeyPem();
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(_path, export);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            return newKey;
        }
        catch
        {
            newKey.Dispose();
            throw;
        }
    }
}
