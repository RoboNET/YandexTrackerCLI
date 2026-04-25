namespace YandexTrackerCLI.Core.Tests.Http;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using TUnit.Core;
using YandexTrackerCLI.Core.Api.Errors;
using YandexTrackerCLI.Core.Auth;
using YandexTrackerCLI.Core.Config;
using YandexTrackerCLI.Core.Http;

public sealed class HandlersTests
{
    // --- AuthHandler ---

    [Test]
    public async Task AuthHandler_AddsAuthorizationHeader_FromProvider()
    {
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new AuthHandler(new OAuthProvider("y0_abc")) { InnerHandler = inner };
        using var client = new HttpClient(handler);

        using var _ = await client.GetAsync("https://api.tracker.yandex.net/v3/myself");

        var seen = inner.Seen[0];
        await Assert.That(seen.Headers.Authorization).IsNotNull();
        await Assert.That(seen.Headers.Authorization!.Scheme).IsEqualTo("OAuth");
        await Assert.That(seen.Headers.Authorization.Parameter).IsEqualTo("y0_abc");
    }

    // --- OrgHeaderHandler ---

    [Test]
    [Arguments(OrgType.Yandex360, "X-Org-ID")]
    [Arguments(OrgType.Cloud, "X-Cloud-Org-ID")]
    public async Task OrgHeaderHandler_AddsCorrectHeaderByOrgType(OrgType type, string expectedHeader)
    {
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new OrgHeaderHandler(type, "org-1") { InnerHandler = inner };
        using var client = new HttpClient(handler);

        _ = await client.GetAsync("https://api.tracker.yandex.net/v3/myself");

        var req = inner.Seen[0];
        await Assert.That(req.Headers.Contains(expectedHeader)).IsTrue();
        await Assert.That(req.Headers.GetValues(expectedHeader).Single()).IsEqualTo("org-1");
    }

    [Test]
    public async Task OrgHeaderHandler_RejectsCrlfInjection_InCtor()
    {
        var ex = Assert.Throws<TrackerException>(
            () => new OrgHeaderHandler(OrgType.Cloud, "123\r\nX-Admin: true"));
        await Assert.That(ex.Code).IsEqualTo(ErrorCode.ConfigError);
    }

    [Test]
    public async Task OrgHeaderHandler_RejectsEmptyOrgId()
    {
        var ex = Assert.Throws<TrackerException>(() => new OrgHeaderHandler(OrgType.Cloud, ""));
        await Assert.That(ex.Code).IsEqualTo(ErrorCode.ConfigError);
    }

    // --- ReadOnlyGuardHandler ---

    [Test]
    [Arguments("POST")]
    [Arguments("PUT")]
    [Arguments("PATCH")]
    [Arguments("DELETE")]
    public async Task ReadOnlyGuard_Blocks_MutatingMethod_WithTrackerException(string method)
    {
        var inner = new TestHttpMessageHandler();
        var handler = new ReadOnlyGuardHandler(enabled: true) { InnerHandler = inner };
        using var client = new HttpClient(handler);

        using var req = new HttpRequestMessage(new HttpMethod(method), "https://api.tracker.yandex.net/v3/issues");
        var ex = await Assert.ThrowsAsync<TrackerException>(() => client.SendAsync(req));
        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.ReadOnlyMode);
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ReadOnlyGuard_Allows_Get_WhenEnabled()
    {
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new ReadOnlyGuardHandler(enabled: true) { InnerHandler = inner };
        using var client = new HttpClient(handler);

        _ = await client.GetAsync("https://api.tracker.yandex.net/v3/myself");
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ReadOnlyGuard_Allows_PostToSearchEndpoint_WhenEnabled()
    {
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new ReadOnlyGuardHandler(enabled: true) { InnerHandler = inner };
        using var client = new HttpClient(handler);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.tracker.yandex.net/v3/issues/_search");
        req.Content = new StringContent("""{"query":"q"}""", Encoding.UTF8, "application/json");

        using var resp = await client.SendAsync(req);
        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ReadOnlyGuard_Blocks_PostToNonSearchEndpoint_WhenEnabled()
    {
        var inner = new TestHttpMessageHandler();
        var handler = new ReadOnlyGuardHandler(enabled: true) { InnerHandler = inner };
        using var client = new HttpClient(handler);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.tracker.yandex.net/v3/issues");
        var ex = await Assert.ThrowsAsync<TrackerException>(() => client.SendAsync(req));
        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.ReadOnlyMode);
        await Assert.That(inner.Seen.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ReadOnlyGuard_Disabled_AllowsMutations()
    {
        var inner = new TestHttpMessageHandler().Push(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new ReadOnlyGuardHandler(enabled: false) { InnerHandler = inner };
        using var client = new HttpClient(handler);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.tracker.yandex.net/v3/issues");
        _ = await client.SendAsync(req);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    // --- RetryHandler ---

    [Test]
    public async Task Retry_On429_WithRetryAfter_EventuallySucceeds()
    {
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Respond(HttpStatusCode.TooManyRequests, TimeSpan.FromMilliseconds(5)))
             .Push(_ => Respond(HttpStatusCode.TooManyRequests, TimeSpan.FromMilliseconds(5)))
             .Push(_ => Respond(HttpStatusCode.OK));
        var handler = new RetryHandler(maxAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(1), cap: TimeSpan.FromMilliseconds(20))
            { InnerHandler = inner };
        using var client = new HttpClient(handler);

        using var resp = await client.GetAsync("https://api.tracker.yandex.net/v3/x");
        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Seen.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Retry_On5xx_Eventual_OrGivesUp_AfterMaxAttempts()
    {
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Respond((HttpStatusCode)502))
             .Push(_ => Respond((HttpStatusCode)502))
             .Push(_ => Respond((HttpStatusCode)502));
        var handler = new RetryHandler(maxAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(1), cap: TimeSpan.FromMilliseconds(5))
            { InnerHandler = inner };
        using var client = new HttpClient(handler);

        using var resp = await client.GetAsync("https://api.tracker.yandex.net/v3/x");
        await Assert.That((int)resp.StatusCode).IsEqualTo(502);
        await Assert.That(inner.Seen.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Retry_DoesNotRetry_On400()
    {
        var inner = new TestHttpMessageHandler().Push(_ => Respond(HttpStatusCode.BadRequest));
        var handler = new RetryHandler(3, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(5))
            { InnerHandler = inner };
        using var client = new HttpClient(handler);

        using var resp = await client.GetAsync("https://api.tracker.yandex.net/v3/x");
        await Assert.That((int)resp.StatusCode).IsEqualTo(400);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RetryHandler_DoesNotRetry_MultipartContent_On5xx()
    {
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var handler = new RetryHandler(
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(1),
            cap: TimeSpan.FromMilliseconds(5))
        { InnerHandler = inner };
        using var client = new HttpClient(handler);

        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.tracker.yandex.net/v3/issues/X/attachments");
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("data"), "file", "f.txt");
        req.Content = multipart;

        using var resp = await client.SendAsync(req);
        await Assert.That((int)resp.StatusCode).IsEqualTo(502);
        await Assert.That(inner.Seen.Count).IsEqualTo(1); // no retry
    }

    [Test]
    public async Task RetryHandler_DoesNotRetry_StreamContent_On5xx()
    {
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var handler = new RetryHandler(
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(1),
            cap: TimeSpan.FromMilliseconds(5))
        { InnerHandler = inner };
        using var client = new HttpClient(handler);

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.tracker.yandex.net/v3/upload");
        req.Content = new StreamContent(new MemoryStream(new byte[] { 1, 2, 3 }));

        using var resp = await client.SendAsync(req);
        await Assert.That((int)resp.StatusCode).IsEqualTo(502);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, TimeSpan? retryAfter = null)
    {
        var r = new HttpResponseMessage(status);
        if (retryAfter is { } d)
        {
            r.Headers.RetryAfter = new RetryConditionHeaderValue(d);
        }

        return r;
    }
}
