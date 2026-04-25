using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Link;

using System.Net;
using System.Net.Http;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt link remove &lt;issue-key&gt; &lt;link-id&gt;</c>:
/// путь <c>DELETE /v3/issues/{key}/links/{link-id}</c>, success-маркер при пустом
/// теле и маппинг 404 → exit 5. Мутируют глобальное state (env + Console + AsyncLocal),
/// поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class LinkRemoveCommandTests
{
    /// <summary>
    /// <c>204 No Content</c> — stdout содержит JSON <c>{"removed":"42"}</c>, method = DELETE,
    /// path заканчивается на <c>/issues/DEV-1/links/42</c>, exit 0.
    /// </summary>
    [Test]
    public async Task LinkRemove_204_PrintsRemovedEnvelope()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        HttpMethod? method = null;
        string? path = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            method = req.Method;
            path = req.RequestUri!.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "link", "remove", "DEV-1", "42" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(method).IsEqualTo(HttpMethod.Delete);
        await Assert.That(path!.EndsWith("/issues/DEV-1/links/42", StringComparison.Ordinal)).IsTrue();

        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("removed").GetString()).IsEqualTo("42");
    }

    /// <summary>
    /// Ответ <c>404 Not Found</c> → exit 5, stderr содержит структурированный JSON с
    /// <c>error.code == "not_found"</c>.
    /// </summary>
    [Test]
    public async Task LinkRemove_404_ReturnsNotFound_Exit5()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        env.InnerHandler = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "link", "remove", "DEV-NOPE", "999" }, sw, er);

        await Assert.That(exit).IsEqualTo(5);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("not_found");
    }
}
