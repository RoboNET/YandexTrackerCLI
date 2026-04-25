namespace YandexTrackerCLI.Tests.Commands;

using System.Text.Json;
using TUnit.Core;

/// <summary>
/// End-to-end тесты команды <c>yt auth logout</c>: удаление токена и inline PEM
/// с сохранением метаданных профиля и идентификаторов сервис-аккаунта.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class AuthLogoutCommandTests
{
    /// <summary>
    /// OAuth-профиль → после logout <c>auth.token</c> удалён, <c>org_id</c> и
    /// <c>auth.type</c> сохранены.
    /// </summary>
    [Test]
    public async Task Logout_OAuth_RemovesToken_KeepsOrgMetadata()
    {
        using var env = new TestEnv();
        env.SetConfig("""
        {"default_profile":"work","profiles":{
          "work":{"org_type":"cloud","org_id":"o","read_only":false,
                  "auth":{"type":"oauth","token":"y0_X"}}}}
        """);
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "auth", "logout", "--profile", "work" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var work = saved.RootElement.GetProperty("profiles").GetProperty("work");
        await Assert.That(work.GetProperty("auth").TryGetProperty("token", out _)).IsFalse();
        await Assert.That(work.GetProperty("org_id").GetString()).IsEqualTo("o");
        await Assert.That(work.GetProperty("auth").GetProperty("type").GetString()).IsEqualTo("oauth");
    }

    /// <summary>
    /// Service-account профиль → logout (без <c>--profile</c>, используется default)
    /// сохраняет <c>service_account_id</c>/<c>key_id</c>/<c>private_key_path</c>
    /// и удаляет <c>private_key_pem</c>.
    /// </summary>
    [Test]
    public async Task Logout_ServiceAccount_KeepsKeyMetadata_RemovesPrivateKeyPem()
    {
        using var env = new TestEnv();
        env.SetConfig("""
        {"default_profile":"ci","profiles":{
          "ci":{"org_type":"cloud","org_id":"o","read_only":true,
                "auth":{"type":"service-account","service_account_id":"sa-1","key_id":"k-1",
                        "private_key_path":"/k.pem","private_key_pem":"SECRET"}}}}
        """);
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "auth", "logout" }, sw, er);
        await Assert.That(exit).IsEqualTo(0);

        using var saved = JsonDocument.Parse(File.ReadAllText(env.ConfigPath));
        var ci = saved.RootElement.GetProperty("profiles").GetProperty("ci").GetProperty("auth");
        await Assert.That(ci.GetProperty("service_account_id").GetString()).IsEqualTo("sa-1");
        await Assert.That(ci.GetProperty("key_id").GetString()).IsEqualTo("k-1");
        await Assert.That(ci.GetProperty("private_key_path").GetString()).IsEqualTo("/k.pem");
        await Assert.That(ci.TryGetProperty("private_key_pem", out _)).IsFalse();
    }

    /// <summary>
    /// Logout на несуществующем профиле → <c>config_error</c> (exit 9).
    /// </summary>
    [Test]
    public async Task Logout_UnknownProfile_ReturnsConfigError()
    {
        using var env = new TestEnv();
        env.SetConfig("""{"default_profile":"a","profiles":{"a":{"org_type":"cloud","org_id":"o","read_only":false,"auth":{"type":"oauth","token":"t"}}}}""");
        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "auth", "logout", "--profile", "does-not-exist" }, sw, er);
        await Assert.That(exit).IsEqualTo(9);
    }
}
