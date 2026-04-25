using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt user me</c>: полный маршрут через
/// <see cref="YandexTrackerCLI.Commands.User.UserMeCommand"/>, <see cref="TrackerContextFactory"/>,
/// подменённый HTTP-handler (через <see cref="TestEnv.InnerHandler"/>).
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class UserMeCommandTests
{
    /// <summary>
    /// Успешный ответ <c>200 OK</c> с JSON — exit 0, stdout содержит сырой JSON из ответа API.
    /// </summary>
    [Test]
    public async Task UserMe_ReturnsRawJsonFromApi()
    {
        using var env = new TestEnv();
        env.SetConfig("""
        {"default_profile":"a","profiles":{"a":{"org_type":"cloud","org_id":"o","read_only":false,
          "auth":{"type":"oauth","token":"y0_X"}}}}
        """);

        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("""{"self":"https://.../myself","login":"me","uid":42}""",
                Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "user", "me" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("login").GetString()).IsEqualTo("me");
        await Assert.That(doc.RootElement.GetProperty("uid").GetInt32()).IsEqualTo(42);

        var req = inner.Seen.Single();
        await Assert.That(req.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(req.RequestUri!.AbsolutePath.EndsWith("/myself", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// Ответ <c>401 Unauthorized</c> → exit 4 (<see cref="YandexTrackerCLI.Core.Api.Errors.ErrorCode.AuthFailed"/>),
    /// stderr содержит структурированный JSON с <c>error.code == "auth_failed"</c>.
    /// </summary>
    [Test]
    public async Task UserMe_401_ReturnsAuthFailed_Exit4()
    {
        using var env = new TestEnv();
        env.SetConfig("""
        {"default_profile":"a","profiles":{"a":{"org_type":"cloud","org_id":"o","read_only":false,
          "auth":{"type":"oauth","token":"y0_X"}}}}
        """);
        env.InnerHandler = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "user", "me" }, sw, er);

        await Assert.That(exit).IsEqualTo(4);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("auth_failed");
    }
}
