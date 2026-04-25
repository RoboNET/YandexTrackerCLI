namespace YandexTrackerCLI.Core.Tests.Config;

using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Core.Config;
using Json;

public sealed class ConfigFileSerializationTests
{
    [Test]
    public async Task RoundTrip_OAuthProfile_Serializes_WithSnakeCaseKeys()
    {
        var cfg = new ConfigFile(
            DefaultProfile: "work",
            Profiles: new Dictionary<string, Profile>
            {
                ["work"] = new Profile(
                    OrgType: OrgType.Yandex360,
                    OrgId: "123456",
                    ReadOnly: false,
                    Auth: new AuthConfig(AuthType.OAuth, Token: "y0_tok")),
            });

        var json = JsonSerializer.Serialize(cfg, TrackerJsonContext.Default.ConfigFile);

        await Assert.That(json).Contains("\"default_profile\": \"work\"");
        await Assert.That(json).Contains("\"org_type\": \"yandex360\"");
        await Assert.That(json).Contains("\"read_only\": false");
        await Assert.That(json).Contains("\"type\": \"oauth\"");

        var back = JsonSerializer.Deserialize(json, TrackerJsonContext.Default.ConfigFile);
        await Assert.That(back!.Profiles["work"].Auth.Token).IsEqualTo("y0_tok");
    }

    [Test]
    public async Task RoundTrip_ServiceAccountProfile_Preserves_KeyFilePath()
    {
        var cfg = new ConfigFile(
            DefaultProfile: "ci",
            Profiles: new Dictionary<string, Profile>
            {
                ["ci"] = new Profile(
                    OrgType: OrgType.Cloud,
                    OrgId: "b1g_123",
                    ReadOnly: true,
                    Auth: new AuthConfig(
                        AuthType.ServiceAccount,
                        ServiceAccountId: "ajeXXXX",
                        KeyId: "ajkXXXX",
                        PrivateKeyPath: "/secrets/sa.pem")),
            });

        var json = JsonSerializer.Serialize(cfg, TrackerJsonContext.Default.ConfigFile);
        var back = JsonSerializer.Deserialize(json, TrackerJsonContext.Default.ConfigFile);

        var p = back!.Profiles["ci"];
        await Assert.That(p.Auth.Type).IsEqualTo(AuthType.ServiceAccount);
        await Assert.That(p.Auth.ServiceAccountId).IsEqualTo("ajeXXXX");
        await Assert.That(p.Auth.KeyId).IsEqualTo("ajkXXXX");
        await Assert.That(p.Auth.PrivateKeyPath).IsEqualTo("/secrets/sa.pem");
        await Assert.That(p.ReadOnly).IsTrue();
    }

    [Test]
    public async Task RoundTrip_IamStaticProfile_WireValue_Uses_Dash()
    {
        var cfg = new ConfigFile("x", new Dictionary<string, Profile>
        {
            ["x"] = new Profile(OrgType.Cloud, "o", false,
                new AuthConfig(AuthType.IamStatic, Token: "t1.Y"))
        });
        var json = JsonSerializer.Serialize(cfg, TrackerJsonContext.Default.ConfigFile);
        await Assert.That(json).Contains("\"type\": \"iam-static\"");
    }
}
