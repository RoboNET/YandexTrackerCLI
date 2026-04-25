namespace YandexTrackerCLI.Tests.Auth.Federated;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Auth.Federated;
using YandexTrackerCLI.Core.Auth;

/// <summary>
/// Юнит-тесты <see cref="DPoPProof"/>: структура compact-JWT, валидность подписи,
/// присутствие JWK с координатами в header.
/// </summary>
public sealed class DPoPProofTests
{
    [Test]
    public async Task Build_ReturnsThreeSegmentCompactJwt()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var proof = DPoPProof.Build(key, "POST", "https://example.test/token");

        var segments = proof.Split('.');
        await Assert.That(segments.Length).IsEqualTo(3);
        await Assert.That(segments[0].Length).IsGreaterThan(0);
        await Assert.That(segments[1].Length).IsGreaterThan(0);
        await Assert.That(segments[2].Length).IsGreaterThan(0);
    }

    [Test]
    public async Task Build_SignatureVerifiesWithSameKey_OverSigningInput()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var proof = DPoPProof.Build(key, "GET", "https://api.example.test/resource");

        var segments = proof.Split('.');
        var signingInput = Encoding.ASCII.GetBytes($"{segments[0]}.{segments[1]}");
        var signature = DecodeBase64Url(segments[2]);

        var ok = key.VerifyData(
            signingInput,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        await Assert.That(ok).IsTrue();
    }

    [Test]
    public async Task Build_HeaderContainsEs256Jwk_WithXandY()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(false);
        var expectedX = Base64Url.Encode(parameters.Q.X!);
        var expectedY = Base64Url.Encode(parameters.Q.Y!);

        var proof = DPoPProof.Build(key, "POST", "https://auth.example.test/token");

        var headerJson = Encoding.UTF8.GetString(DecodeBase64Url(proof.Split('.')[0]));
        using var doc = JsonDocument.Parse(headerJson);

        await Assert.That(doc.RootElement.GetProperty("alg").GetString()).IsEqualTo("ES256");
        await Assert.That(doc.RootElement.GetProperty("typ").GetString()).IsEqualTo("dpop+jwt");
        var jwk = doc.RootElement.GetProperty("jwk");
        await Assert.That(jwk.GetProperty("kty").GetString()).IsEqualTo("EC");
        await Assert.That(jwk.GetProperty("crv").GetString()).IsEqualTo("P-256");
        await Assert.That(jwk.GetProperty("x").GetString()).IsEqualTo(expectedX);
        await Assert.That(jwk.GetProperty("y").GetString()).IsEqualTo(expectedY);
    }

    [Test]
    public async Task ComputeJktThumbprint_ReturnsBase64UrlEncodedSha512_OfCanonicalJwk()
    {
        // Yandex Cloud's authorization server uses SHA-512 over the canonical JWK
        // (NOT the RFC 7638 default of SHA-256). Verified by reproducing the
        // server-side `dpop_credential_id` from a real `yc`-issued refresh token:
        // only SHA-512 over the canonical JWK matches the 64-byte server thumbprint.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(false);
        var x = Base64Url.Encode(p.Q.X!);
        var y = Base64Url.Encode(p.Q.Y!);

        // Reconstruct the canonical JSON in lex order (crv, kty, x, y), no whitespace.
        var expectedJson = $"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{x}\",\"y\":\"{y}\"}}";
        var expectedHash = SHA512.HashData(Encoding.UTF8.GetBytes(expectedJson));
        var expectedThumbprint = Base64Url.Encode(expectedHash);

        var actual = DPoPProof.ComputeJktThumbprint(key);

        await Assert.That(actual).IsEqualTo(expectedThumbprint);
    }

    /// <summary>
    /// SHA-512 produces a 64-byte digest, base64url-encoded without padding to 86
    /// characters. SHA-256 by contrast yields 43 characters. A length check ≥ 80
    /// is sufficient to detect an accidental regression to SHA-256.
    /// </summary>
    [Test]
    public async Task ComputeJktThumbprint_ProducesSha512Length()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var thumbprint = DPoPProof.ComputeJktThumbprint(key);

        await Assert.That(thumbprint.Length).IsEqualTo(86);
        await Assert.That(thumbprint.Length).IsGreaterThanOrEqualTo(80);
    }

    /// <summary>
    /// Reference vector: a real RSA JWK from a Yandex Cloud `yc`-issued refresh
    /// token, paired with the SHA-512 thumbprint extracted from its
    /// <c>dpop_credential_id</c> claim. Locks in the canonical JWK byte sequence
    /// (lex order: <c>e, kty, n</c>, no whitespace, base64url no padding) and
    /// guarantees byte-identical thumbprint computation against yc's server.
    /// </summary>
    [Test]
    public async Task ComputeJktThumbprint_MatchesYcReferenceVector()
    {
        const string nB64Url =
            "60wyctqP6tn23OddXcSawGxAeDQLr1tjAfVNG02EKj3sdYRXDwKWd_r7Fy1VmY7C22rkxJTtV9zGo2hi6nR447sCLaVpJSu02x7NBQF8Xxu5W89FNgf8EdGOKlbsvdNiwWUPRezwQOTYSjJUiUJCqtRkhaLd0HVtu42Irn0kZc4qFXf16im4DwZ_FrKQ5UsyLHtrSWxoey0qw5KvIM2DzpyBbgPQMugUSyi4j-jWtdCwBYubnY9k2ZOFC-_8eX0KiG-7pNwnXaJPEsL-EtoyrV3WxBOa3weqhfqAYhX_1Iau5jF1M2A2G7gf6CUNA89o-FGEfdKG-bDASfgR68pxZQ";
        const string eB64Url = "AQAB";
        const string expectedThumbprint =
            "c4TgV_pclae22Z1wbqAm4YuexEuEYViSBFGH-dGoT8SUS1B0Gvr_ZlglK-mEyK1CLCiXrfFCt1ox5ejp8BhcPA";

        var rsaParams = new RSAParameters
        {
            Modulus = DecodeBase64Url(nB64Url),
            Exponent = DecodeBase64Url(eB64Url),
        };

        using var rsa = RSA.Create();
        rsa.ImportParameters(rsaParams);

        var actual = DPoPProof.ComputeJktThumbprint(rsa);

        await Assert.That(actual).IsEqualTo(expectedThumbprint);
        await Assert.That(actual.Length).IsEqualTo(86);
    }

    [Test]
    public async Task ComputeJktThumbprint_IsDeterministic_OnRepeatedCalls()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var t1 = DPoPProof.ComputeJktThumbprint(key);
        var t2 = DPoPProof.ComputeJktThumbprint(key);
        var t3 = DPoPProof.ComputeJktThumbprint(key);

        await Assert.That(t1).IsEqualTo(t2);
        await Assert.That(t2).IsEqualTo(t3);
        await Assert.That(t1.Length).IsGreaterThan(0);
        // Base64url has no padding chars.
        await Assert.That(t1.Contains('=')).IsFalse();
        await Assert.That(t1.Contains('+')).IsFalse();
        await Assert.That(t1.Contains('/')).IsFalse();
    }

    [Test]
    public async Task ComputeJktThumbprint_DifferentKeys_ProduceDifferentThumbprints()
    {
        using var key1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var key2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var t1 = DPoPProof.ComputeJktThumbprint(key1);
        var t2 = DPoPProof.ComputeJktThumbprint(key2);

        await Assert.That(t1).IsNotEqualTo(t2);
    }

    [Test]
    public async Task ComputeJktThumbprint_NullEcKey_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Task.FromResult(DPoPProof.ComputeJktThumbprint((ECDsa)null!)));
    }

    [Test]
    public async Task ComputeJktThumbprint_NullRsaKey_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Task.FromResult(DPoPProof.ComputeJktThumbprint((RSA)null!)));
    }

    /// <summary>
    /// RFC 7638: JWK members must be in lexicographic order — for an EC key that's
    /// <c>crv, kty, x, y</c>. Yandex Cloud's verifier hashes the proof's <c>jwk</c>
    /// header as-is to derive a thumbprint, so the order in the header must match
    /// the order we use in <see cref="DPoPProof.ComputeJktThumbprint"/>.
    /// </summary>
    [Test]
    public async Task Build_HeaderJwkInLexicographicOrder()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var proof = DPoPProof.Build(key, "POST", "https://auth.example.test/token");

        var headerJson = Encoding.UTF8.GetString(DecodeBase64Url(proof.Split('.')[0]));
        using var doc = JsonDocument.Parse(headerJson);
        var jwkMembers = string.Join(
            ',',
            doc.RootElement.GetProperty("jwk").EnumerateObject().Select(p => p.Name));

        // Order-sensitive comparison: lex order is crv, kty, x, y.
        await Assert.That(jwkMembers).IsEqualTo("crv,kty,x,y");
    }

    /// <summary>
    /// Verifies the raw header JSON contains <c>"typ":"dpop+jwt"</c> with a literal
    /// <c>+</c> rather than the JSON-escaped <c>+</c>. Some JOSE verifiers compare
    /// the typ string byte-for-byte against the RFC 9449 example, which uses the
    /// unescaped form.
    /// </summary>
    [Test]
    public async Task Build_TypHeaderUsesLiteralPlus()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var proof = DPoPProof.Build(key, "POST", "https://auth.example.test/token");

        var headerBytes = DecodeBase64Url(proof.Split('.')[0]);
        var headerJson = Encoding.UTF8.GetString(headerBytes);

        await Assert.That(headerJson.Contains("\"typ\":\"dpop+jwt\"")).IsTrue();
        await Assert.That(headerJson.Contains("\\u002B")).IsFalse();
        await Assert.That(headerJson.Contains("dpop\\u002Bjwt")).IsFalse();
    }

    /// <summary>
    /// Sanity-check: SHA-512 over the raw JWK bytes embedded in the proof header
    /// (with the same canonical JSON encoding we use everywhere) must equal
    /// <see cref="DPoPProof.ComputeJktThumbprint(ECDsa)"/> output. If a verifier
    /// hashes the proof's jwk as-is, this guarantees they get the same thumbprint
    /// as what we advertised in <c>dpop_jkt</c>. Yandex Cloud uses SHA-512 instead
    /// of the RFC 7638 default of SHA-256.
    /// </summary>
    [Test]
    public async Task Build_JwkHeaderBytes_HashIdenticallyToComputedThumbprint()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var proof = DPoPProof.Build(key, "POST", "https://auth.example.test/token");

        var headerJson = Encoding.UTF8.GetString(DecodeBase64Url(proof.Split('.')[0]));
        using var doc = JsonDocument.Parse(headerJson);
        var jwkRaw = doc.RootElement.GetProperty("jwk").GetRawText();

        // GetRawText preserves the exact byte sequence we emitted (lex order, no
        // whitespace, UnsafeRelaxedJsonEscaping). Hashing those bytes must match
        // the canonical thumbprint independently produced by ComputeJktThumbprint.
        var hash = SHA512.HashData(Encoding.UTF8.GetBytes(jwkRaw));
        var thumbprintFromHeader = Base64Url.Encode(hash);

        var thumbprintFromKey = DPoPProof.ComputeJktThumbprint(key);

        await Assert.That(thumbprintFromHeader).IsEqualTo(thumbprintFromKey);
    }

    private static byte[] DecodeBase64Url(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }
}
