namespace YandexTrackerCLI.Auth.Federated;

using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using YandexTrackerCLI.Core.Auth;

/// <summary>
/// Builds RFC 9449 DPoP compact JWTs (header.payload.signature) using ES256.
/// </summary>
/// <remarks>
/// The produced JWT carries a <c>jwk</c> header with the sender's public key so the
/// resource server can verify possession without prior enrollment. Each proof is
/// single-use: <c>jti</c> is a random <see cref="Guid"/> and <c>iat</c> is a current
/// timestamp. An optional server-supplied <c>nonce</c> is included when present
/// (used to resume after a <c>401</c> + <c>DPoP-Nonce</c> challenge).
/// </remarks>
public static class DPoPProof
{
    /// <summary>
    /// JSON writer options used for both the proof header/payload and the canonical
    /// JWK thumbprint input. <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>
    /// keeps characters such as <c>+</c> literal (not escaped to <c>+</c>) so the
    /// emitted bytes match the RFC 9449 example (<c>"typ":"dpop+jwt"</c>) and any
    /// verifier hashing the JWK as-is computes the same SHA-512 thumbprint we do.
    /// Safe here because the bytes are base64url-encoded inside a JWS, not embedded
    /// in HTML or JS.
    /// </summary>
    private static readonly JsonWriterOptions JsonWriter = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Builds a compact DPoP JWT bound to <paramref name="httpMethod"/> and
    /// <paramref name="httpUrl"/>, signed with <paramref name="key"/> over SHA-256.
    /// </summary>
    /// <param name="key">ECDSA P-256 private key. Not disposed by this method.</param>
    /// <param name="httpMethod">HTTP method the proof is bound to (normalized to upper-case).</param>
    /// <param name="httpUrl">Absolute HTTP URL the proof is bound to.</param>
    /// <param name="nonce">Optional server-supplied nonce (included as <c>nonce</c> claim).</param>
    /// <returns>A three-segment compact JWT string usable as the <c>DPoP:</c> header value.</returns>
    public static string Build(
        ECDsa key,
        string httpMethod,
        string httpUrl,
        string? nonce = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(httpMethod);
        ArgumentException.ThrowIfNullOrWhiteSpace(httpUrl);

        var parameters = key.ExportParameters(includePrivateParameters: false);
        var x = Base64Url.Encode(parameters.Q.X!);
        var y = Base64Url.Encode(parameters.Q.Y!);

        var headerBytes = WriteHeaderJson(x, y);
        var payloadBytes = WritePayloadJson(
            htu: httpUrl,
            htm: httpMethod.ToUpperInvariant(),
            iat: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            jti: Guid.NewGuid().ToString("N"),
            nonce: nonce);

        var headerPart = Base64Url.Encode(headerBytes);
        var payloadPart = Base64Url.Encode(payloadBytes);

        var signingInput = Encoding.ASCII.GetBytes($"{headerPart}.{payloadPart}");
        var signature = key.SignData(
            signingInput,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{headerPart}.{payloadPart}.{Base64Url.Encode(signature)}";
    }

    /// <summary>
    /// Computes the JWK SHA-512 thumbprint (<c>jkt</c>) of <paramref name="key"/>'s
    /// public component. Used for the <c>dpop_jkt</c> authorize-request parameter so the
    /// authorization server can bind subsequent tokens to this key.
    /// </summary>
    /// <param name="key">ECDSA P-256 key. Only the public coordinates are read.</param>
    /// <returns>Base64url-encoded SHA-512 hash of the canonical JWK (no padding, 86 chars).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <c>null</c>.</exception>
    /// <remarks>
    /// RFC 7638 specifies SHA-256 by default, but Yandex Cloud's authorization server
    /// uses SHA-512 instead (verified by reproducing <c>dpop_credential_id</c> from a
    /// real <c>yc</c>-issued refresh token: only SHA-512 over the canonical JWK
    /// matches the 64-byte server-side thumbprint). The canonical JWK is the JSON
    /// object containing only the required members (<c>crv</c>, <c>kty</c>, <c>x</c>,
    /// <c>y</c>) in lexicographic order, with no insignificant whitespace.
    /// Coordinates are base64url-encoded without padding — matching the encoding
    /// used in the <c>jwk</c> header of the DPoP proof itself, so a verifier
    /// computing the thumbprint over either representation will agree.
    /// </remarks>
    public static string ComputeJktThumbprint(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var parameters = key.ExportParameters(includePrivateParameters: false);
        var x = Base64Url.Encode(parameters.Q.X!);
        var y = Base64Url.Encode(parameters.Q.Y!);

        // RFC 7638: only required members, lexicographic order, no whitespace.
        // For an EC key the required members are: crv, kty, x, y.
        // Use the shared writer options (UnsafeRelaxedJsonEscaping) so the bytes
        // hashed here are byte-identical to the JWK we emit in the proof header.
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, JsonWriter))
        {
            w.WriteStartObject();
            w.WriteString("crv", "P-256");
            w.WriteString("kty", "EC");
            w.WriteString("x", x);
            w.WriteString("y", y);
            w.WriteEndObject();
        }

        return ComputeThumbprintFromCanonicalJwk(ms.ToArray());
    }

    /// <summary>
    /// Computes the JWK SHA-512 thumbprint (<c>jkt</c>) of an RSA key's public component.
    /// Used to match the Yandex Cloud server-side thumbprint produced for RSA-bound
    /// DPoP credentials (verified against a real <c>yc</c>-issued refresh token).
    /// </summary>
    /// <param name="key">RSA key. Only the public modulus and exponent are read.</param>
    /// <returns>Base64url-encoded SHA-512 hash of the canonical JWK (no padding, 86 chars).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <c>null</c>.</exception>
    /// <remarks>
    /// The canonical JWK for an RSA key contains only the required members
    /// (<c>e</c>, <c>kty</c>, <c>n</c>) in lexicographic order, base64url-encoded
    /// without padding, with no insignificant whitespace.
    /// </remarks>
    public static string ComputeJktThumbprint(RSA key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var parameters = key.ExportParameters(includePrivateParameters: false);
        var e = Base64Url.Encode(TrimLeadingZeros(parameters.Exponent!));
        var n = Base64Url.Encode(TrimLeadingZeros(parameters.Modulus!));

        // RFC 7638: only required members, lexicographic order, no whitespace.
        // For an RSA key the required members are: e, kty, n.
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, JsonWriter))
        {
            w.WriteStartObject();
            w.WriteString("e", e);
            w.WriteString("kty", "RSA");
            w.WriteString("n", n);
            w.WriteEndObject();
        }

        return ComputeThumbprintFromCanonicalJwk(ms.ToArray());
    }

    /// <summary>
    /// Hashes a pre-built canonical JWK byte sequence with SHA-512 and returns the
    /// base64url-encoded digest. Exposed as <c>internal</c> so unit tests can supply
    /// hand-built canonical JWK bytes against known reference vectors without
    /// reconstructing key material.
    /// </summary>
    /// <param name="canonicalJwkBytes">UTF-8 bytes of the canonical JWK JSON.</param>
    /// <returns>Base64url-encoded SHA-512 hash (no padding, 86 chars).</returns>
    internal static string ComputeThumbprintFromCanonicalJwk(byte[] canonicalJwkBytes)
    {
        ArgumentNullException.ThrowIfNull(canonicalJwkBytes);
        var hash = SHA512.HashData(canonicalJwkBytes);
        return Base64Url.Encode(hash);
    }

    /// <summary>
    /// Strips leading zero-bytes from an RSA <see cref="RSAParameters"/> field.
    /// .NET sometimes pads <c>Modulus</c> or <c>Exponent</c> with a leading 0x00 to
    /// preserve sign for ASN.1 INTEGER decoding; the canonical JWK form (RFC 7518
    /// §6.3.1) requires the unpadded big-endian byte sequence.
    /// </summary>
    private static byte[] TrimLeadingZeros(byte[] data)
    {
        var i = 0;
        while (i < data.Length - 1 && data[i] == 0)
        {
            i++;
        }

        if (i == 0)
        {
            return data;
        }

        var trimmed = new byte[data.Length - i];
        Buffer.BlockCopy(data, i, trimmed, 0, trimmed.Length);
        return trimmed;
    }

    private static byte[] WriteHeaderJson(string x, string y)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, JsonWriter))
        {
            w.WriteStartObject();
            w.WriteString("alg", "ES256");
            w.WriteString("typ", "dpop+jwt");

            // RFC 7638 lexicographic order for the embedded JWK: crv, kty, x, y.
            // Matches the exact byte sequence ComputeJktThumbprint hashes, so a
            // verifier hashing the proof's jwk as-is gets the same thumbprint.
            w.WriteStartObject("jwk");
            w.WriteString("crv", "P-256");
            w.WriteString("kty", "EC");
            w.WriteString("x", x);
            w.WriteString("y", y);
            w.WriteEndObject();
            w.WriteEndObject();
        }

        return ms.ToArray();
    }

    private static byte[] WritePayloadJson(string htu, string htm, long iat, string jti, string? nonce)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, JsonWriter))
        {
            w.WriteStartObject();
            w.WriteString("htu", htu);
            w.WriteString("htm", htm);
            w.WriteNumber("iat", iat);
            w.WriteString("jti", jti);
            if (nonce is not null)
            {
                w.WriteString("nonce", nonce);
            }

            w.WriteEndObject();
        }

        return ms.ToArray();
    }
}
