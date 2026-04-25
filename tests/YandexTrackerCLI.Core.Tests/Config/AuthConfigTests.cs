namespace YandexTrackerCLI.Core.Tests.Config;

using TUnit.Core;
using YandexTrackerCLI.Core.Config;

public sealed class AuthConfigTests
{
    [Test]
    public async Task ToString_MasksOAuthToken()
    {
        var a = new AuthConfig(AuthType.OAuth, Token: "y0_SUPER_SECRET");
        var s = a.ToString();
        await Assert.That(s).DoesNotContain("y0_SUPER_SECRET");
        await Assert.That(s).Contains("Token = ***");
    }

    [Test]
    public async Task ToString_MasksPrivateKeyPem()
    {
        var a = new AuthConfig(AuthType.ServiceAccount,
            ServiceAccountId: "sa",
            KeyId: "k",
            PrivateKeyPem: "-----BEGIN PRIVATE KEY-----\nSECRET");
        var s = a.ToString();
        await Assert.That(s).DoesNotContain("SECRET");
        await Assert.That(s).DoesNotContain("BEGIN PRIVATE");
        await Assert.That(s).Contains("PrivateKeyPem = ***");
    }

    [Test]
    public async Task ToString_DoesNotMask_PrivateKeyPath()
    {
        // путь не секрет, его можно логировать
        var a = new AuthConfig(AuthType.ServiceAccount,
            ServiceAccountId: "sa",
            KeyId: "k",
            PrivateKeyPath: "/secrets/sa.pem");
        var s = a.ToString();
        await Assert.That(s).Contains("/secrets/sa.pem");
    }

    [Test]
    public async Task ToString_MasksIamStaticToken()
    {
        var a = new AuthConfig(AuthType.IamStatic, Token: "t1.XXXX.SENSITIVE");
        var s = a.ToString();
        await Assert.That(s).DoesNotContain("SENSITIVE");
        await Assert.That(s).Contains("Token = ***");
    }
}
