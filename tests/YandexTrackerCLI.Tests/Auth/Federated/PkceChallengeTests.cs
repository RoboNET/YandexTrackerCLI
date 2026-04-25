namespace YandexTrackerCLI.Tests.Auth.Federated;

using System.Security.Cryptography;
using System.Text;
using TUnit.Core;
using YandexTrackerCLI.Auth.Federated;

/// <summary>
/// Юнит-тесты фабрики <see cref="PkceChallengeFactory"/>: проверяют длину verifier
/// (RFC 7636 bounds), корректность вычисления challenge и случайность.
/// </summary>
public sealed class PkceChallengeTests
{
    [Test]
    public async Task Generate_Verifier_Length_BetweenRfcBounds()
    {
        var c = PkceChallengeFactory.Generate();
        await Assert.That(c.Verifier.Length).IsGreaterThanOrEqualTo(43);
        await Assert.That(c.Verifier.Length).IsLessThanOrEqualTo(128);
    }

    [Test]
    public async Task Generate_Challenge_IsSha256_Base64UrlOf_Verifier()
    {
        var c = PkceChallengeFactory.Generate();
        var expected = Core.Auth.Base64Url.Encode(
            SHA256.HashData(Encoding.ASCII.GetBytes(c.Verifier)));
        await Assert.That(c.Challenge).IsEqualTo(expected);
    }

    [Test]
    public async Task Generate_TwoCallsProduceDifferentVerifiers()
    {
        var a = PkceChallengeFactory.Generate();
        var b = PkceChallengeFactory.Generate();
        await Assert.That(a.Verifier).IsNotEqualTo(b.Verifier);
    }
}
