namespace YandexTrackerCLI.Core.Tests.Http;

using System.Net;
using System.Text;
using TUnit.Core;
using YandexTrackerCLI.Core.Api.Errors;
using YandexTrackerCLI.Core.Http;

/// <summary>
/// Тесты HTTP-guard'а политики <c>external_effects</c>: запрет призыва и мутаций
/// автоматизаций при неограниченном чтении.
/// </summary>
public sealed class ExternalEffectsGuardHandlerTests
{
    private const string Base = "https://api.tracker.yandex.net/v3/";

    private static (HttpClient Client, TestHttpMessageHandler Inner) Build(bool allowed)
    {
        var inner = new TestHttpMessageHandler();
        var handler = new ExternalEffectsGuardHandler(allowed, "ci") { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new Uri(Base) };
        return (client, inner);
    }

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

    private static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");

    /// <summary>
    /// Призыв в теле комментария запрещён.
    /// </summary>
    [Test]
    [Arguments("""{"text":"hi","summonees":["user"]}""", "summonees")]
    [Arguments("""{"text":"hi","maillistSummonees":["ml@example.com"]}""", "maillistSummonees")]
    [Arguments("""{"text":"hi","SUMMONEES":["user"]}""", "summonees")]
    public async Task Summon_InCommentBody_IsBlocked(string body, string field)
    {
        var (client, inner) = Build(allowed: false);

        var ex = await Assert.ThrowsAsync<TrackerException>(async () =>
            await client.PostAsync("issues/DEV-42/comments", Json(body)));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
        await Assert.That(ex.Message).Contains($"'{field}'");
        await Assert.That(ex.Message).Contains("external_effects");
        await Assert.That(ex.Message).Contains("profile 'ci'");
        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    /// <summary>
    /// Призыв во вложенном объекте (комментарий внутри тела перехода) тоже запрещён:
    /// поиск рекурсивный, иначе вложение было бы обходом.
    /// </summary>
    [Test]
    public async Task Summon_InNestedObject_IsBlocked()
    {
        var (client, inner) = Build(allowed: false);

        var ex = await Assert.ThrowsAsync<TrackerException>(async () =>
            await client.PostAsync(
                "issues/DEV-42/transitions/close/_execute",
                Json("""{"comment":{"text":"done","summonees":["user"]}}""")));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
        await Assert.That(ex.Message).Contains("'summonees'");
        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    /// <summary>
    /// Guard стоит на HTTP-слое и видит тело независимо от команды: призыв внутри
    /// <c>bulkchange</c> запрещён так же, как и в комментарии.
    /// </summary>
    [Test]
    public async Task Summon_InBulkchangeBody_IsBlocked()
    {
        var (client, inner) = Build(allowed: false);

        var ex = await Assert.ThrowsAsync<TrackerException>(async () =>
            await client.PostAsync(
                "bulkchange/_update",
                Json("""{"issues":["DEV-1"],"values":{"comment":{"summonees":["user"]}}}""")));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
        await Assert.That(ex.Message).Contains("'summonees'");
        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    /// <summary>
    /// Сам по себе <c>bulkchange</c> вне объёма политики: без призыва в теле проходит.
    /// </summary>
    [Test]
    public async Task Bulkchange_WithoutSummon_PassesThrough()
    {
        var (client, inner) = Build(allowed: false);
        inner.Push(_ => Ok());

        using var resp = await client.PostAsync(
            "bulkchange/_update",
            Json("""{"issues":["DEV-1"],"values":{"priority":"critical"}}"""));

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        client.Dispose();
    }

    /// <summary>
    /// Мутации автоматизаций запрещены всеми мутирующими методами: триггер и автодействие
    /// переживают сессию и умеют сами ходить наружу.
    /// </summary>
    [Test]
    [Arguments("queues/DEV/triggers")]
    [Arguments("queues/DEV/triggers/7")]
    [Arguments("queues/DEV/autoactions")]
    [Arguments("queues/DEV/autoactions/7")]
    public async Task AutomationMutation_IsBlocked(string path)
    {
        var (client, inner) = Build(allowed: false);

        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            using var req = new HttpRequestMessage(method, path)
            {
                Content = Json("""{"name":"t"}"""),
            };
            var ex = await Assert.ThrowsAsync<TrackerException>(async () => await client.SendAsync(req));

            await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
            await Assert.That(ex.Message).Contains("external_effects");
            await Assert.That(ex.Message).Contains("profile 'ci'");
        }

        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    /// <summary>
    /// Чтение автоматизаций остаётся разрешённым — политика ограничивает только запись.
    /// </summary>
    [Test]
    [Arguments("queues/DEV/triggers")]
    [Arguments("queues/DEV/triggers/7")]
    [Arguments("queues/DEV/autoactions")]
    public async Task AutomationRead_PassesThrough(string path)
    {
        var (client, inner) = Build(allowed: false);
        inner.Push(_ => Ok());

        using var resp = await client.GetAsync(path);

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        client.Dispose();
    }

    /// <summary>
    /// Percent-encoding и регистр сегмента не дают обхода: сравнение идёт после
    /// декодирования.
    /// </summary>
    [Test]
    [Arguments("queues/DEV/%74riggers")]
    [Arguments("queues/DEV/TRIGGERS")]
    [Arguments("queues/DEV/auto%61ctions")]
    public async Task PercentEncodedSegment_DoesNotBypassTheGuard(string path)
    {
        var (client, inner) = Build(allowed: false);

        var ex = await Assert.ThrowsAsync<TrackerException>(async () =>
            await client.PostAsync(path, Json("""{"name":"t"}""")));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    /// <summary>
    /// <c>POST .../_search</c> — семантически чтение, проходит (то же исключение, что и
    /// в остальных guard'ах).
    /// </summary>
    [Test]
    public async Task PostSearch_PassesThrough()
    {
        var (client, inner) = Build(allowed: false);
        inner.Push(_ => Ok());

        using var resp = await client.PostAsync("issues/_search", Json("""{"query":"Updated: today()"}"""));

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        client.Dispose();
    }

    /// <summary>
    /// Обычная запись без призыва и без автоматизаций проходит: политика узкая.
    /// </summary>
    [Test]
    public async Task PlainWrite_PassesThrough()
    {
        var (client, inner) = Build(allowed: false);
        inner.Push(_ => Ok());

        using var resp = await client.PostAsync("issues/DEV-42/comments", Json("""{"text":"hi"}"""));

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        client.Dispose();
    }

    /// <summary>
    /// Тело переживает инспекцию: inner-handler получает его целиком.
    /// </summary>
    [Test]
    public async Task InspectedBody_IsStillDelivered()
    {
        var (client, inner) = Build(allowed: false);
        string? seenBody = null;
        inner.Push(req =>
        {
            seenBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok();
        });

        using var resp = await client.PostAsync("issues/DEV-42/comments", Json("""{"text":"hi"}"""));

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(seenBody).IsEqualTo("""{"text":"hi"}""");
        client.Dispose();
    }

    /// <summary>
    /// Тело, объявленное как JSON, но неразбираемое — отказ: доказать безопасность нельзя.
    /// </summary>
    [Test]
    public async Task MalformedJsonBody_IsBlocked()
    {
        var (client, inner) = Build(allowed: false);

        var ex = await Assert.ThrowsAsync<TrackerException>(async () =>
            await client.PostAsync("issues/DEV-42/comments", Json("""{"text": """)));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
        await Assert.That(ex.Message).Contains("external_effects");
        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    /// <summary>
    /// Не-JSON тело (загрузка вложения) не инспектируется и проходит.
    /// </summary>
    [Test]
    public async Task NonJsonBody_PassesThrough()
    {
        var (client, inner) = Build(allowed: false);
        inner.Push(_ => Ok());

        using var content = new ByteArrayContent(new byte[] { 1, 2, 3 });
        using var resp = await client.PostAsync("issues/DEV-42/attachments", content);

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        client.Dispose();
    }

    /// <summary>
    /// При разрешённых внешних эффектах guard инертен: проходит всё перечисленное выше.
    /// </summary>
    [Test]
    [Arguments("issues/DEV-42/comments", """{"text":"hi","summonees":["user"]}""")]
    [Arguments("queues/DEV/triggers", """{"name":"t"}""")]
    [Arguments("queues/DEV/autoactions/7", """{"name":"a"}""")]
    [Arguments("issues/DEV-42/comments", """{"text": """)]
    public async Task ExternalEffectsAllowed_EverythingPassesThrough(string path, string body)
    {
        var (client, inner) = Build(allowed: true);
        inner.Push(_ => Ok());

        using var resp = await client.PostAsync(path, Json(body));

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        client.Dispose();
    }
}
