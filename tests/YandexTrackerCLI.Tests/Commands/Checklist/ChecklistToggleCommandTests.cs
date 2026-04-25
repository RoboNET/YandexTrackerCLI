using YandexTrackerCLI.Tests.Http;

namespace YandexTrackerCLI.Tests.Commands.Checklist;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;

/// <summary>
/// End-to-end тесты команды <c>yt checklist toggle &lt;issue-key&gt; &lt;item-id&gt;</c>:
/// два режима — явный <c>--checked true|false</c> (один PATCH) и автоинверсия
/// (GET списка → PATCH с инвертированным <c>checked</c>). Плюс обработка отсутствия
/// пункта в списке (<see cref="YandexTrackerCLI.Core.Api.Errors.ErrorCode.NotFound"/>, exit 5).
/// Мутируют глобальное state (env + Console + AsyncLocal), поэтому последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ChecklistToggleCommandTests
{
    /// <summary>
    /// С явным <c>--checked true</c> — выполняется ровно один HTTP-запрос (PATCH), тело
    /// <c>{"checked":true}</c>, без предварительного GET.
    /// </summary>
    [Test]
    public async Task Toggle_WithExplicitChecked_PatchesDirectly()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        string? capturedBody = null;
        HttpMethod? capturedMethod = null;
        string? capturedPath = null;
        var inner = new TestHttpMessageHandler().Push(req =>
        {
            capturedMethod = req.Method;
            capturedPath = req.RequestUri!.AbsolutePath;
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"i1","checked":true}""", Encoding.UTF8, "application/json"),
            };
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(
            new[] { "checklist", "toggle", "DEV-1", "i1", "--checked", "true" },
            sw,
            er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Patch);
        await Assert.That(capturedPath!.EndsWith("/issues/DEV-1/checklistItems/i1", StringComparison.Ordinal)).IsTrue();
        using var doc = JsonDocument.Parse(capturedBody!);
        await Assert.That(doc.RootElement.GetProperty("checked").GetBoolean()).IsTrue();
    }

    /// <summary>
    /// Без <c>--checked</c> — сначала GET списка, затем PATCH с инвертированным состоянием:
    /// текущее <c>checked:false</c> → PATCH <c>{"checked":true}</c>.
    /// </summary>
    [Test]
    public async Task Toggle_WithoutChecked_GetsListThenInverts()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        string? patchBody = null;
        HttpMethod? patchMethod = null;
        string? patchPath = null;

        var inner = new TestHttpMessageHandler()
            .Push(_ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """[{"id":"i1","checked":false},{"id":"i2","checked":true}]""",
                        Encoding.UTF8,
                        "application/json"),
                };
                return r;
            })
            .Push(req =>
            {
                patchMethod = req.Method;
                patchPath = req.RequestUri!.AbsolutePath;
                patchBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                var r = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"id":"i1","checked":true}""", Encoding.UTF8, "application/json"),
                };
                return r;
            });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "checklist", "toggle", "DEV-1", "i1" }, sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(inner.Seen.Count).IsEqualTo(2);
        await Assert.That(inner.Seen[0].Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(patchMethod).IsEqualTo(HttpMethod.Patch);
        await Assert.That(patchPath!.EndsWith("/issues/DEV-1/checklistItems/i1", StringComparison.Ordinal)).IsTrue();
        using var doc = JsonDocument.Parse(patchBody!);
        await Assert.That(doc.RootElement.GetProperty("checked").GetBoolean()).IsTrue();
    }

    /// <summary>
    /// Пункта с заданным <c>item-id</c> нет в списке — команда возвращает exit 5
    /// (<c>not_found</c>) до выполнения PATCH.
    /// </summary>
    [Test]
    public async Task Toggle_ItemNotFound_Returns_Exit5()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler().Push(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"id":"other","checked":false}]""",
                    Encoding.UTF8,
                    "application/json"),
            };
            return r;
        });
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(new[] { "checklist", "toggle", "DEV-1", "i1" }, sw, er);

        await Assert.That(exit).IsEqualTo(5);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
        using var doc = JsonDocument.Parse(er.ToString());
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetString())
            .IsEqualTo("not_found");
    }
}
