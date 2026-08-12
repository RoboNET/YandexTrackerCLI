namespace YandexTrackerCLI.Core.Tests.Http;

using System.Net;
using System.Text;
using TUnit.Core;
using YandexTrackerCLI.Core.Api.Errors;
using YandexTrackerCLI.Core.Http;

/// <summary>
/// Тесты HTTP-guard'а политики <c>allowed_queues</c>: разбор уже собранного URL запроса.
/// </summary>
public sealed class AllowedQueuesGuardHandlerTests
{
    private const string Base = "https://api.tracker.yandex.net/v3/";

    private static (HttpClient Client, TestHttpMessageHandler Inner) Build(params string[] allowed)
    {
        var inner = new TestHttpMessageHandler();
        var handler = new AllowedQueuesGuardHandler(allowed, "ci") { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new Uri(Base) };
        return (client, inner);
    }

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

    [Test]
    [Arguments("issues/DEV-1")]
    [Arguments("issues/DEV-1/comments")]
    [Arguments("issues/DEV-1/attachments")]
    [Arguments("issues/DEV-1/checklist")]
    [Arguments("issues/DEV-1/links")]
    [Arguments("issues/DEV-1/worklog")]
    [Arguments("issues/DEV-1/transitions")]
    [Arguments("queues/DEV")]
    [Arguments("queues/DEV/triggers")]
    public async Task AllowedQueue_PassesThrough(string path)
    {
        var (client, inner) = Build("DEV", "QA");
        inner.Push(_ => Ok());

        using var resp = await client.GetAsync(path);

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        client.Dispose();
    }

    [Test]
    [Arguments("issues/OPS-1")]
    [Arguments("issues/OPS-1/comments")]
    [Arguments("queues/OPS")]
    [Arguments("queues/OPS/autoactions")]
    public async Task ForbiddenQueue_IsBlocked_WithPolicyViolation(string path)
    {
        var (client, inner) = Build("DEV", "QA");

        var ex = await Assert.ThrowsAsync<TrackerException>(async () => await client.GetAsync(path));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
        await Assert.That(ex.Message)
            .IsEqualTo("queue 'OPS' is outside allowed_queues of profile 'ci' (allowed: DEV, QA)");
        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    /// <summary>
    /// Запрет действует и на запись: мутирующий запрос к чужой очереди тоже блокируется.
    /// </summary>
    [Test]
    public async Task ForbiddenQueue_IsBlocked_ForMutatingRequests()
    {
        var (client, inner) = Build("DEV");

        var ex = await Assert.ThrowsAsync<TrackerException>(async () =>
            await client.PostAsync(
                "issues/OPS-1/comments",
                new StringContent("""{"text":"hi"}""", Encoding.UTF8, "application/json")));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    /// <summary>
    /// Ключ в URL percent-encoded (команды зовут <see cref="Uri.EscapeDataString"/>) —
    /// guard декодирует сегменты перед разбором, поэтому обход не работает.
    /// </summary>
    [Test]
    [Arguments("issues/%4FPS-1")]
    [Arguments("issues/OPS%2D1")]
    [Arguments("queues/%4FPS")]
    public async Task PercentEncodedKey_DoesNotBypassTheGuard(string path)
    {
        var (client, inner) = Build("DEV");

        var ex = await Assert.ThrowsAsync<TrackerException>(async () => await client.GetAsync(path));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    /// <summary>
    /// Пути без ключа задачи проходят: ограничение поиска решается пост-фильтрацией в CLI.
    /// </summary>
    [Test]
    [Arguments("issues/_search?perPage=50&page=1")]
    [Arguments("myself")]
    [Arguments("users")]
    [Arguments("fields")]
    [Arguments("boards")]
    [Arguments("entities/project/_search")]
    public async Task PathsWithoutIssueKey_PassThrough(string path)
    {
        var (client, inner) = Build("DEV");
        inner.Push(_ => Ok());

        using var resp = await client.GetAsync(path);

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        client.Dispose();
    }

    /// <summary>
    /// Очередь в query-параметре (например, <c>issues/_suggest?queue=OPS</c>) тоже проверяется.
    /// </summary>
    [Test]
    public async Task QueueQueryParameter_IsChecked()
    {
        var (client, inner) = Build("DEV");

        var ex = await Assert.ThrowsAsync<TrackerException>(async () =>
            await client.GetAsync("issues/_suggest?input=abc&queue=OPS"));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    [Test]
    public async Task QueueQueryParameter_Allowed_PassesThrough()
    {
        var (client, inner) = Build("DEV");
        inner.Push(_ => Ok());

        using var resp = await client.GetAsync("issues/_suggest?input=abc&queue=DEV");

        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        client.Dispose();
    }

    /// <summary>
    /// Регрессия: пустой (или отсутствующий) список = ограничения нет, поведение прежнее.
    /// </summary>
    [Test]
    public async Task EmptyAllowList_IsInert()
    {
        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Ok()).Push(_ => Ok());
        var handler = new AllowedQueuesGuardHandler(null, "ci") { InnerHandler = inner };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(Base) };

        using var a = await client.GetAsync("issues/OPS-1");
        using var b = await client.GetAsync("queues/ANYTHING");

        await Assert.That(a.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(b.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Seen.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ExtractQueues_ReadsBothPathAndQuery()
    {
        var queues = AllowedQueuesGuardHandler.ExtractQueues(
            new Uri("https://api.tracker.yandex.net/v3/issues/DEV-1/comments?queue=OPS"));

        await Assert.That(queues).IsEquivalentTo(new[] { "DEV", "OPS" });
    }

    /// <summary>
    /// Повторённый параметр проверяется целиком: guard защищает конечный запрос, а не
    /// команды, которым доверяет. Остановка на первом совпадении пропускала бы
    /// <c>?queue=DEV&amp;queue=OPS</c> как «DEV».
    /// </summary>
    [Test]
    public async Task RepeatedQueueQueryParameter_AllOccurrencesAreChecked()
    {
        var (client, inner) = Build("DEV");

        var ex = await Assert.ThrowsAsync<TrackerException>(async () =>
            await client.GetAsync("issues/_suggest?input=abc&queue=DEV&queue=OPS"));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.PolicyViolation);
        await Assert.That(ex.Message).Contains("'OPS'");
        await Assert.That(inner.Seen).IsEmpty();
        client.Dispose();
    }

    [Test]
    public async Task ExtractQueues_ReadsEveryQueueQueryParameter()
    {
        var queues = AllowedQueuesGuardHandler.ExtractQueues(
            new Uri("https://api.tracker.yandex.net/v3/issues/_suggest?queue=DEV&queue=OPS&queue=QA"));

        await Assert.That(queues).IsEquivalentTo(new[] { "DEV", "OPS", "QA" });
    }
}
