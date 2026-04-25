using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Ref;

using System.Net;
using System.Net.Http;
using System.Text;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тест команды <c>yt ref resolutions</c>: выполняет
/// <c>GET /v3/resolutions</c> и печатает ответ как есть. Мутирует глобальное
/// state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class RefResolutionsCommandTests
{
    /// <summary>
    /// URL заканчивается на <c>/resolutions</c>, метод GET, exit 0.
    /// </summary>
    [Test]
    public async Task Resolutions_HitsResolutionsPath()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        HttpMethod? capturedMethod = null;
        string? capturedPath = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedMethod = req.Method;
            capturedPath = req.RequestUri!.AbsolutePath;
            var r = new HttpResponseMessage(HttpStatusCode.OK);
            r.Content = new StringContent("[]", Encoding.UTF8, "application/json");
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "ref", "resolutions" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Get);
        await Assert.That(capturedPath!.EndsWith("/resolutions", StringComparison.Ordinal)).IsTrue();
    }
}
