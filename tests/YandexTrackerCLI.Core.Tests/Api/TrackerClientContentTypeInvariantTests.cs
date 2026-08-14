namespace YandexTrackerCLI.Core.Tests.Api;

using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Core.Api;
using Http;

/// <summary>
/// Инвариант, на котором стоит инспекция тела в guard'ах политик: они разбирают тело
/// только когда <c>Content-Type</c> содержит <c>json</c>, а всё остальное пропускают
/// как «загрузку вложения». Это безопасно ровно до тех пор, пока
/// <see cref="TrackerClient"/> остаётся единственным путём к сети и любой его мутирующий
/// запрос либо не несёт тела вовсе, либо несёт JSON, либо является multipart-загрузкой.
/// Тест закрепляет это, чтобы исключение в guard'е было следствием проверенного
/// инварианта, а не молчаливого допущения.
/// </summary>
public sealed class TrackerClientContentTypeInvariantTests
{
    /// <summary>
    /// Мутирующие методы <see cref="TrackerClient"/>, известные этому тесту. Список
    /// сверяется с реальным API (см. <see cref="MutatingApi_IsFullyCovered"/>): новый
    /// мутирующий метод обязан быть добавлен сюда вместе с проверкой его content-type.
    /// </summary>
    private static readonly string[] CoveredMutatingMethods =
    [
        nameof(TrackerClient.PostJsonAsync),
        nameof(TrackerClient.PostJsonRawAsync),
        nameof(TrackerClient.PostJsonRawWithHeadersAsync),
        nameof(TrackerClient.PatchJsonAsync),
        nameof(TrackerClient.DeleteAsync),
        nameof(TrackerClient.PostMultipartAsync),
    ];

    private static readonly string[] ReadOnlyMethods =
    [
        nameof(TrackerClient.GetAsync),
        nameof(TrackerClient.GetStreamingAsync),
        nameof(TrackerClient.GetPagedAsync),
        nameof(TrackerClient.GetCursorPagedAsync),
    ];

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"1"}""", Encoding.UTF8, "application/json"),
        };

    /// <summary>
    /// Каждый мутирующий запрос клиента: либо тела нет, либо это multipart-загрузка,
    /// либо content-type — JSON. Промежуточного «текстового» тела не существует.
    /// </summary>
    [Test]
    public async Task EveryMutatingRequest_IsJsonOrMultipart()
    {
        var inner = new TestHttpMessageHandler();
        for (var i = 0; i < 6; i++)
        {
            inner.Push(_ => Ok());
        }

        using var http = new HttpClient(inner) { BaseAddress = new Uri("https://api.tracker.yandex.net/v3/") };
        var client = new TrackerClient(http);

        using var body = JsonDocument.Parse("""{"summary":"s"}""");
        _ = await client.PostJsonAsync("issues", body.RootElement);
        _ = await client.PostJsonRawAsync("issues", """{"summary":"s"}""");
        _ = await client.PostJsonRawWithHeadersAsync("issues/_search", """{"query":"Key: DEV-1"}""");
        _ = await client.PatchJsonAsync("issues/DEV-1", """{"summary":"s"}""");
        _ = await client.DeleteAsync("issues/DEV-1/comments/1");

        using (var multipart = new MultipartFormDataContent())
        {
            multipart.Add(new ByteArrayContent([1, 2, 3]), name: "file", fileName: "a.bin");
            _ = await client.PostMultipartAsync("issues/DEV-1/attachments", multipart);
        }

        await Assert.That(inner.Seen.Count).IsEqualTo(6);

        foreach (var seen in inner.Seen)
        {
            var mediaType = seen.Content?.Headers.ContentType?.MediaType;
            if (seen.Content is null)
            {
                // Тело отсутствует (DELETE) — инспектировать нечего.
                continue;
            }

            if (seen.Content is MultipartFormDataContent)
            {
                await Assert.That(mediaType!).StartsWith("multipart/");
                continue;
            }

            await Assert.That(mediaType).IsEqualTo("application/json");
        }
    }

    /// <summary>
    /// Список покрытых методов совпадает с публичным API клиента: новый способ отправить
    /// тело наружу не должен появиться незамеченным, иначе guard'ы начнут пропускать
    /// непроверенные тела.
    /// </summary>
    [Test]
    public async Task MutatingApi_IsFullyCovered()
    {
        var actual = typeof(TrackerClient)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var expected = CoveredMutatingMethods
            .Concat(ReadOnlyMethods)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(expected);
    }
}
