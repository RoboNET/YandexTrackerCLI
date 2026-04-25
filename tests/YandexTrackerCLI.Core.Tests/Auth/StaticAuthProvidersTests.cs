namespace YandexTrackerCLI.Core.Tests.Auth;

using TUnit.Core;
using YandexTrackerCLI.Core.Auth;

public sealed class StaticAuthProvidersTests
{
    [Test]
    public async Task OAuth_ProducesOAuthScheme()
    {
        var p = new OAuthProvider("y0_ABC");
        var h = await p.GetAuthorizationAsync(CancellationToken.None);
        await Assert.That(h.Scheme).IsEqualTo("OAuth");
        await Assert.That(h.Parameter).IsEqualTo("y0_ABC");
    }

    [Test]
    public async Task IamStatic_ProducesBearerScheme()
    {
        var p = new IamStaticProvider("t1.XXXX");
        var h = await p.GetAuthorizationAsync(CancellationToken.None);
        await Assert.That(h.Scheme).IsEqualTo("Bearer");
        await Assert.That(h.Parameter).IsEqualTo("t1.XXXX");
    }
}
