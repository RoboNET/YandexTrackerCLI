namespace YandexTrackerCLI.Core.Tests.Http;

using System.Net;
using System.Text;
using TUnit.Core;
using YandexTrackerCLI.Core.Api.Errors;
using YandexTrackerCLI.Core.Http;

/// <summary>
/// Тесты HTTP-guard'а политики <c>allowed_write_issues</c>: ограничение области записи
/// списком задач при неограниченном чтении.
/// </summary>
public sealed class AllowedWriteIssuesGuardHandlerTests
{
    private const string Base = "https://api.tracker.yandex.net/v3/";

    private static (HttpClient Client, TestHttpMessageHandler Inner) Build(params string[] allowed)
    {
        var inner = new TestHttpMessageHandler();
        var handler = new AllowedWriteIssuesGuardHandler(allowed, "ci") { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new Uri(Base) };
        return (client, inner);
    }

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

    private static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");

    /// <summary>
    /// Запись в разрешённую задачу и в её под-ресурсы проходит.
    /// </summary>
    [Test]
    [Arguments("issues/DEV-42/comments")]
    [Arguments("issues/DEV-42/checklistItems")]
    [Arguments("issues/DEV-42/links")]
    [Arguments("issues/DEV-42/worklog")]
    public async Task AllowedIssue_Write_PassesThrough(string path)
    {
        var (client, inner) = Build("DEV-42");
        inner.Push(_ => Ok());

        using var resp = await client.PostAsync(path, Json("""{"text":"hi"}"""));

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        client.Dispose();
    }

    /// <summary>
    /// Тело запроса переживает инспекцию: inner-handler получает его целиком.
    /// </summary>
    [Test]
    public async Task AllowedIssue_Write_BodyIsStillDelivered()
    {
        var (client, inner) = Build("DEV-42");
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
    /// Запись в задачу вне списка — отказ по политике, запрос до сети не доходит.
    /// </summary>
    [Test]
    public async Task ForbiddenIssue_Write_IsBlocked()
    {
        var (client, inner) = Build("DEV-42");

        var ex = await Assert.ThrowsAsync<TrackerException>(async () =>
            await client.PostAsync("issues/OPS-7/comments", Json("""{"text":"hi"}""")));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
        await Assert.That(ex.Message)
            .IsEqualTo("issue 'OPS-7' is outside allowed_write_issues of profile 'ci' (allowed: DEV-42)");
        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    /// <summary>
    /// Все мутирующие методы одинаково ограничены.
    /// </summary>
    [Test]
    public async Task ForbiddenIssue_AllMutatingMethods_AreBlocked()
    {
        var (client, inner) = Build("DEV-42");

        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            using var req = new HttpRequestMessage(method, "issues/OPS-7");
            var ex = await Assert.ThrowsAsync<TrackerException>(async () => await client.SendAsync(req));
            await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
        }

        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    /// <summary>
    /// Ключевое отличие от read-only: чтение задачи вне списка продолжает работать.
    /// </summary>
    [Test]
    [Arguments("issues/OPS-7")]
    [Arguments("issues/OPS-7/comments")]
    [Arguments("queues/OPS")]
    public async Task ForeignIssue_Read_PassesThrough(string path)
    {
        var (client, inner) = Build("DEV-42");
        inner.Push(_ => Ok());

        using var resp = await client.GetAsync(path);

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        client.Dispose();
    }

    /// <summary>
    /// <c>POST .../_search</c> — семантически чтение, проходит (то же исключение, что и
    /// в read-only guard'е).
    /// </summary>
    [Test]
    [Arguments("issues/_search")]
    [Arguments("entities/project/_search")]
    public async Task PostSearch_PassesThrough(string path)
    {
        var (client, inner) = Build("DEV-42");
        inner.Push(_ => Ok());

        using var resp = await client.PostAsync(path, Json("""{"query":"Updated: today()"}"""));

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        client.Dispose();
    }

    /// <summary>
    /// Мутации, не привязанные к конкретной разрешённой задаче, запрещены целиком:
    /// создание задачи, bulkchange, мутации очередей и автоматизаций.
    /// </summary>
    [Test]
    [Arguments("issues")]
    [Arguments("bulkchange")]
    [Arguments("bulkchange/_move")]
    [Arguments("queues/DEV/triggers")]
    [Arguments("queues")]
    public async Task UnscopedMutation_IsBlocked(string path)
    {
        var (client, inner) = Build("DEV-42");

        var ex = await Assert.ThrowsAsync<TrackerException>(async () =>
            await client.PostAsync(path, Json("""{"summary":"s"}""")));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
        await Assert.That(ex.Message).Contains("is not scoped to a single issue");
        await Assert.That(ex.Message).Contains("profile 'ci'");
        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    /// <summary>
    /// Percent-encoding и регистр не дают обхода.
    /// </summary>
    [Test]
    [Arguments("issues/%4FPS-7/comments")]
    [Arguments("issues/OPS%2D7/comments")]
    public async Task PercentEncodedKey_DoesNotBypassTheGuard(string path)
    {
        var (client, inner) = Build("DEV-42");

        var ex = await Assert.ThrowsAsync<TrackerException>(async () =>
            await client.PostAsync(path, Json("""{"text":"hi"}""")));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    /// <summary>
    /// Сравнение ключей регистронезависимое: <c>dev-42</c> — та же задача, что <c>DEV-42</c>.
    /// </summary>
    [Test]
    public async Task IssueKeyComparison_IsCaseInsensitive()
    {
        var (client, inner) = Build("DEV-42");
        inner.Push(_ => Ok());

        using var resp = await client.PostAsync("issues/dev-42/comments", Json("""{"text":"hi"}"""));

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        client.Dispose();
    }

    /// <summary>
    /// Поля рассылки уведомлений в теле комментария запрещены при действующей политике —
    /// даже когда сама задача разрешена.
    /// </summary>
    [Test]
    [Arguments("""{"text":"hi","summonees":["user"]}""", "summonees")]
    [Arguments("""{"text":"hi","maillistSummonees":["ml@example.com"]}""", "maillistSummonees")]
    [Arguments("""{"text":"hi","SUMMONEES":["user"]}""", "summonees")]
    public async Task NotificationFieldsInBody_AreBlocked(string body, string field)
    {
        var (client, inner) = Build("DEV-42");

        var ex = await Assert.ThrowsAsync<TrackerException>(async () =>
            await client.PostAsync("issues/DEV-42/comments", Json(body)));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
        await Assert.That(ex.Message).Contains($"'{field}'");
        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    /// <summary>
    /// Поле рассылки во вложенном объекте (например, комментарий при выполнении перехода)
    /// тоже отлавливается.
    /// </summary>
    [Test]
    public async Task NestedNotificationField_IsBlocked()
    {
        var (client, inner) = Build("DEV-42");

        var ex = await Assert.ThrowsAsync<TrackerException>(async () =>
            await client.PostAsync(
                "issues/DEV-42/transitions/close/_execute",
                Json("""{"comment":{"text":"done","summonees":["user"]}}""")));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    /// <summary>
    /// Не-JSON тело (например, загрузка вложения) не разбирается — проверяется только URL.
    /// </summary>
    [Test]
    public async Task NonJsonBody_IsNotInspected()
    {
        var (client, inner) = Build("DEV-42");
        inner.Push(_ => Ok());

        var content = new ByteArrayContent(new byte[] { 1, 2, 3 });
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var resp = await client.PostAsync("issues/DEV-42/attachments", content);

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        client.Dispose();
    }

    /// <summary>
    /// Регрессия: без списка guard инертен — и запись, и создание задачи проходят как раньше.
    /// </summary>
    [Test]
    public async Task EmptyAllowList_IsInert()
    {
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Ok()).Push(_ => Ok()).Push(_ => Ok());
        var handler = new AllowedWriteIssuesGuardHandler(null, "ci") { InnerHandler = inner };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(Base) };

        using var a = await client.PostAsync("issues/OPS-7/comments", Json("""{"text":"hi","summonees":["u"]}"""));
        using var b = await client.PostAsync("issues", Json("""{"summary":"s"}"""));
        using var c = await client.PostAsync("bulkchange", Json("""{"issues":["OPS-7"]}"""));

        await Assert.That(a.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(b.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(c.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Seen.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ExtractIssueKeys_ReadsKeyFromPath()
    {
        var keys = AllowedWriteIssuesGuardHandler.ExtractIssueKeys(
            new Uri("https://api.tracker.yandex.net/v3/issues/DEV-42/comments"));

        await Assert.That(keys).IsEquivalentTo(new[] { "DEV-42" });
    }

    [Test]
    public async Task ExtractIssueKeys_SkipsSubResources()
    {
        var keys = AllowedWriteIssuesGuardHandler.ExtractIssueKeys(
            new Uri("https://api.tracker.yandex.net/v3/issues/_search?perPage=50"));

        await Assert.That(keys).IsEmpty();
    }
}
