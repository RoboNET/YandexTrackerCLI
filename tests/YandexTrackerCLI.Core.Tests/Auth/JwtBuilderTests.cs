namespace YandexTrackerCLI.Core.Tests.Auth;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Core.Auth;

public sealed class JwtBuilderTests
{
    [Test]
    public async Task Build_EmitsThreeDotSeparatedSegments_WithPs256Signature_VerifiableWithSameKey()
    {
        using var rsa = RSA.Create(2048);

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var ttl = TimeSpan.FromHours(1);

        var jwt = JwtBuilder.Build(
            rsa: rsa,
            keyId: "key-1",
            issuer: "sa-1",
            audience: "https://iam.api.cloud.yandex.net/iam/v1/tokens",
            issuedAt: issuedAt,
            ttl: ttl);

        var parts = jwt.Split('.');
        await Assert.That(parts.Length).IsEqualTo(3);

        var header = JsonDocument.Parse(DecodeToString(parts[0])).RootElement;
        await Assert.That(header.GetProperty("alg").GetString()).IsEqualTo("PS256");
        await Assert.That(header.GetProperty("kid").GetString()).IsEqualTo("key-1");
        await Assert.That(header.GetProperty("typ").GetString()).IsEqualTo("JWT");

        var payload = JsonDocument.Parse(DecodeToString(parts[1])).RootElement;
        await Assert.That(payload.GetProperty("iss").GetString()).IsEqualTo("sa-1");
        await Assert.That(payload.GetProperty("aud").GetString()).IsEqualTo("https://iam.api.cloud.yandex.net/iam/v1/tokens");
        await Assert.That(payload.GetProperty("iat").GetInt64()).IsEqualTo(issuedAt.ToUnixTimeSeconds());
        await Assert.That(payload.GetProperty("exp").GetInt64()).IsEqualTo(issuedAt.Add(ttl).ToUnixTimeSeconds());

        var signedData = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = DecodeToBytes(parts[2]);
        var ok = rsa.VerifyData(signedData, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        await Assert.That(ok).IsTrue();
    }

    [Test]
    public async Task Build_NoPaddingCharsInAnySegment()
    {
        using var rsa = RSA.Create(2048);
        var jwt = JwtBuilder.Build(rsa, "k", "i", "a", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        await Assert.That(jwt).DoesNotContain("=");
    }

    private static string DecodeToString(string urlSafe) => Encoding.UTF8.GetString(DecodeToBytes(urlSafe));

    private static byte[] DecodeToBytes(string urlSafe)
    {
        var padded = urlSafe.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "=";  break;
        }
        return Convert.FromBase64String(padded);
    }
}
