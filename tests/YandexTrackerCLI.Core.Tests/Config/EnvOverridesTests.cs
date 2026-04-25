namespace YandexTrackerCLI.Core.Tests.Config;

using TUnit.Core;
using YandexTrackerCLI.Core.Api.Errors;
using YandexTrackerCLI.Core.Config;

public sealed class EnvOverridesTests
{
    private static ConfigFile EmptyCfg() => new("default", new());

    private static ConfigFile CfgWith(Profile p, string name = "default") =>
        new(name, new Dictionary<string, Profile> { [name] = p });

    [Test]
    public async Task Resolve_NoEnv_NoProfileMatch_ThrowsConfigError()
    {
        var env = new Dictionary<string, string?>();
        var ex = Assert.Throws<TrackerException>(
            () => EnvOverrides.Resolve(EmptyCfg(), profileName: null, env));
        await Assert.That(ex.Code).IsEqualTo(ErrorCode.ConfigError);
    }

    [Test]
    public async Task Resolve_OAuthTokenFromEnv_OverridesType()
    {
        var cfg = CfgWith(new Profile(OrgType.Cloud, "org", false,
            new AuthConfig(AuthType.IamStatic, Token: "old")));
        var env = new Dictionary<string, string?>
        {
            ["YT_OAUTH_TOKEN"] = "y0_new",
            ["YT_ORG_ID"] = "org",
            ["YT_ORG_TYPE"] = "cloud",
        };

        var eff = EnvOverrides.Resolve(cfg, null, env);

        await Assert.That(eff.Auth.Type).IsEqualTo(AuthType.OAuth);
        await Assert.That(eff.Auth.Token).IsEqualTo("y0_new");
    }

    [Test]
    public async Task Resolve_ServiceAccountPriority_OverOAuthAndIam()
    {
        var env = new Dictionary<string, string?>
        {
            ["YT_OAUTH_TOKEN"] = "ignored",
            ["YT_IAM_TOKEN"] = "also-ignored",
            ["YT_SERVICE_ACCOUNT_ID"] = "sa",
            ["YT_SERVICE_ACCOUNT_KEY_ID"] = "kid",
            ["YT_SERVICE_ACCOUNT_KEY_FILE"] = "/tmp/k.pem",
            ["YT_ORG_ID"] = "org",
            ["YT_ORG_TYPE"] = "cloud",
        };

        var eff = EnvOverrides.Resolve(EmptyCfg(), null, env);

        await Assert.That(eff.Auth.Type).IsEqualTo(AuthType.ServiceAccount);
        await Assert.That(eff.Auth.ServiceAccountId).IsEqualTo("sa");
        await Assert.That(eff.Auth.KeyId).IsEqualTo("kid");
        await Assert.That(eff.Auth.PrivateKeyPath).IsEqualTo("/tmp/k.pem");
    }

    [Test]
    public async Task Resolve_PartialServiceAccountEnv_ThrowsConfigError()
    {
        var env = new Dictionary<string, string?>
        {
            ["YT_SERVICE_ACCOUNT_ID"] = "sa",
            ["YT_SERVICE_ACCOUNT_KEY_FILE"] = "/tmp/k.pem",
            ["YT_ORG_ID"] = "org",
            ["YT_ORG_TYPE"] = "cloud",
        };
        var ex = Assert.Throws<TrackerException>(
            () => EnvOverrides.Resolve(EmptyCfg(), null, env));
        await Assert.That(ex.Code).IsEqualTo(ErrorCode.ConfigError);
    }

    [Test]
    public async Task Resolve_ReadOnlyFlag_Wins_WhenAnyLevelEnabled()
    {
        var cfg = CfgWith(new Profile(OrgType.Cloud, "org", ReadOnly: false,
            new AuthConfig(AuthType.OAuth, Token: "y")));
        var env = new Dictionary<string, string?> { ["YT_READ_ONLY"] = "1" };

        var eff = EnvOverrides.Resolve(cfg, null, env, cliReadOnly: false);

        await Assert.That(eff.ReadOnly).IsTrue();
    }

    [Test]
    public async Task Resolve_ReadOnly_FromCliFlag_Wins_OverFalseInFile()
    {
        var cfg = CfgWith(new Profile(OrgType.Cloud, "org", false,
            new AuthConfig(AuthType.OAuth, Token: "y")));
        var eff = EnvOverrides.Resolve(cfg, null, new Dictionary<string, string?>(), cliReadOnly: true);
        await Assert.That(eff.ReadOnly).IsTrue();
    }

    [Test]
    public async Task Resolve_ReadOnlyInProfile_NotOverridden_ByFalseCliOrEnv()
    {
        var cfg = CfgWith(new Profile(OrgType.Cloud, "org", ReadOnly: true,
            new AuthConfig(AuthType.OAuth, Token: "y")));
        var eff = EnvOverrides.Resolve(cfg, null, new Dictionary<string, string?>(), cliReadOnly: false);
        await Assert.That(eff.ReadOnly).IsTrue();
    }

    [Test]
    public async Task Resolve_ProfileFromYtProfileEnv_WhenArgumentNull()
    {
        var cfg = new ConfigFile("default", new Dictionary<string, Profile>
        {
            ["work"] = new Profile(OrgType.Cloud, "o", false, new AuthConfig(AuthType.OAuth, Token: "t")),
        });
        var env = new Dictionary<string, string?> { ["YT_PROFILE"] = "work" };

        var eff = EnvOverrides.Resolve(cfg, null, env);

        await Assert.That(eff.Name).IsEqualTo("work");
    }

    [Test]
    public async Task Resolve_UnknownOrgType_ThrowsConfigError()
    {
        var env = new Dictionary<string, string?>
        {
            ["YT_OAUTH_TOKEN"] = "y",
            ["YT_ORG_ID"] = "o",
            ["YT_ORG_TYPE"] = "something-weird",
        };
        var ex = Assert.Throws<TrackerException>(
            () => EnvOverrides.Resolve(EmptyCfg(), null, env));
        await Assert.That(ex.Code).IsEqualTo(ErrorCode.ConfigError);
    }

    [Test]
    public async Task Resolve_FileAuth_NoEnvOverride_UsesProfileAsIs()
    {
        var profile = new Profile(OrgType.Cloud, "org-from-file", false,
            new AuthConfig(AuthType.OAuth, Token: "file-token"));
        var cfg = CfgWith(profile);

        var eff = EnvOverrides.Resolve(cfg, null, new Dictionary<string, string?>());

        await Assert.That(eff.Auth.Token).IsEqualTo("file-token");
        await Assert.That(eff.OrgId).IsEqualTo("org-from-file");
    }
}
