namespace YandexTrackerCLI.Core.Auth;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Json;

public static class JwtBuilder
{
    public static string Build(
        RSA rsa,
        string keyId,
        string issuer,
        string audience,
        DateTimeOffset issuedAt,
        TimeSpan ttl)
    {
        var header = new JwtHeader(alg: "PS256", kid: keyId, typ: "JWT");
        var payload = new JwtPayload(
            iss: issuer,
            aud: audience,
            iat: issuedAt.ToUnixTimeSeconds(),
            exp: issuedAt.Add(ttl).ToUnixTimeSeconds());

        var headerJson = JsonSerializer.SerializeToUtf8Bytes(header, TrackerJsonContext.Default.JwtHeader);
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(payload, TrackerJsonContext.Default.JwtPayload);

        var headerPart = Base64Url.Encode(headerJson);
        var payloadPart = Base64Url.Encode(payloadJson);

        var signingInput = Encoding.ASCII.GetBytes($"{headerPart}.{payloadPart}");
        var signature = rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var signaturePart = Base64Url.Encode(signature);

        return $"{headerPart}.{payloadPart}.{signaturePart}";
    }
}

internal sealed record JwtHeader(string alg, string kid, string typ);

internal sealed record JwtPayload(string iss, string aud, long iat, long exp);
