namespace YandexTrackerCLI.Auth.Federated;

using System.Security.Cryptography;
using System.Text;
using YandexTrackerCLI.Core.Auth;

/// <summary>
/// PKCE (RFC 7636) пара <c>code_verifier</c> и <c>code_challenge</c>
/// для OAuth authorization code flow с public-клиентом.
/// </summary>
/// <param name="Verifier">Случайный high-entropy verifier (43–128 ASCII символов).</param>
/// <param name="Challenge">Base64Url(SHA256(ASCII(verifier))).</param>
public sealed record PkceChallenge(string Verifier, string Challenge);

/// <summary>
/// Фабрика для генерации <see cref="PkceChallenge"/> с использованием
/// криптостойкого RNG.
/// </summary>
public static class PkceChallengeFactory
{
    /// <summary>
    /// Генерирует новую PKCE-пару: verifier = Base64Url(32 random bytes)
    /// (ровно 43 символа), challenge = Base64Url(SHA256(ASCII(verifier))).
    /// </summary>
    /// <returns>Свежесгенерированная <see cref="PkceChallenge"/>.</returns>
    public static PkceChallenge Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64Url.Encode(bytes);
        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64Url.Encode(challengeBytes);
        return new PkceChallenge(verifier, challenge);
    }
}
