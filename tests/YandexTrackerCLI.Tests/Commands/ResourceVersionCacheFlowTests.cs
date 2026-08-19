namespace YandexTrackerCLI.Tests.Commands;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Commands;
using YandexTrackerCLI.Core.Config;
using YandexTrackerCLI.Core.Storage;
using YandexTrackerCLI.Tests.Http;

/// <summary>
/// End-to-end тесты optimistic locking через кэш версий: <c>get</c> запоминает версию,
/// последующий <c>update</c> подставляет её в <c>?version=N</c>, флаги
/// <c>--version</c>/<c>--no-version-check</c>/<c>--overwrite-latest</c> её переопределяют.
/// </summary>
/// <remarks>
/// Кэш живёт в <c>XDG_CACHE_HOME</c>, который <see cref="TestEnv"/> уводит во временный
/// каталог — реальный <c>$HOME</c> тесты не трогают.
/// </remarks>
[NotInParallel("yt-cli-global-state")]
public sealed class ResourceVersionCacheFlowTests
{
    /// <summary>
    /// <c>issue get</c> запоминает версию, следующий <c>issue update</c> отправляет её в query.
    /// </summary>
    [Test]
    public async Task IssueGetThenUpdate_SendsRememberedVersion()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var queries = new List<string>();
        env.InnerHandler = new TestHttpMessageHandler()
            .Push(req => Json(queries, req, """{"key":"TECH-1","version":7}"""))
            .Push(req => Json(queries, req, """{"key":"TECH-1","version":8}"""));

        var get = await env.Invoke(["issue", "get", "TECH-1"], new StringWriter(), new StringWriter());
        var update = await env.Invoke(
            ["issue", "update", "TECH-1", "--summary", "new"], new StringWriter(), new StringWriter());

        await Assert.That(get).IsEqualTo(0);
        await Assert.That(update).IsEqualTo(0);
        await Assert.That(queries[1]).IsEqualTo("?version=7");
    }

    /// <summary>
    /// Без предшествующего <c>get</c> версия не подставляется: скрипты, зовущие только
    /// <c>update</c>, работают как раньше.
    /// </summary>
    [Test]
    public async Task IssueUpdate_WithoutPriorGet_SendsNoVersion()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var queries = new List<string>();
        env.InnerHandler = new TestHttpMessageHandler()
            .Push(req => Json(queries, req, """{"key":"TECH-1"}"""));

        var exit = await env.Invoke(
            ["issue", "update", "TECH-1", "--summary", "new"], new StringWriter(), new StringWriter());

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(queries[0]).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// <c>--no-version-check</c> игнорирует запомненную версию.
    /// </summary>
    [Test]
    public async Task IssueUpdate_NoVersionCheck_IgnoresCache()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        await Seed(ResourceVersionFlow.IssueResource, "TECH-1", 7);

        var queries = new List<string>();
        env.InnerHandler = new TestHttpMessageHandler()
            .Push(req => Json(queries, req, """{"key":"TECH-1"}"""));

        var exit = await env.Invoke(
            ["issue", "update", "TECH-1", "--summary", "new", "--no-version-check"],
            new StringWriter(), new StringWriter());

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(queries[0]).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// Явный <c>--version</c> перебивает запомненную версию.
    /// </summary>
    [Test]
    public async Task IssueUpdate_ExplicitVersion_BeatsCache()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        await Seed(ResourceVersionFlow.IssueResource, "TECH-1", 7);

        var queries = new List<string>();
        env.InnerHandler = new TestHttpMessageHandler()
            .Push(req => Json(queries, req, """{"key":"TECH-1"}"""));

        var exit = await env.Invoke(
            ["issue", "update", "TECH-1", "--summary", "new", "--version", "42"],
            new StringWriter(), new StringWriter());

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(queries[0]).IsEqualTo("?version=42");
    }

    /// <summary>
    /// Корневое поле <c>version</c> в теле перебивает кэш и вырезается из отправляемого JSON.
    /// </summary>
    [Test]
    public async Task IssueUpdate_BodyVersion_BeatsCacheAndIsStripped()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        await Seed(ResourceVersionFlow.IssueResource, "TECH-1", 7);

        var file = Path.Combine(env.Root, "issue.json");
        await File.WriteAllTextAsync(file, """{"version":3,"summary":"from file"}""");

        var queries = new List<string>();
        string? body = null;
        env.InnerHandler = new TestHttpMessageHandler().Push(req =>
        {
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(queries, req, """{"key":"TECH-1"}""");
        });

        var exit = await env.Invoke(
            ["issue", "update", "TECH-1", "--json-file", file], new StringWriter(), new StringWriter());

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(queries[0]).IsEqualTo("?version=3");
        using var doc = JsonDocument.Parse(body!);
        await Assert.That(doc.RootElement.TryGetProperty("version", out _)).IsFalse();
    }

    /// <summary>
    /// <c>--overwrite-latest</c> перечитывает ресурс и записывает поверх текущей версии.
    /// </summary>
    [Test]
    public async Task IssueUpdate_OverwriteLatest_ReadsCurrentVersionFirst()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        await Seed(ResourceVersionFlow.IssueResource, "TECH-1", 7);

        var methods = new List<HttpMethod>();
        var queries = new List<string>();
        HttpResponseMessage Handle(HttpRequestMessage req)
        {
            methods.Add(req.Method);
            return Json(queries, req, """{"key":"TECH-1","version":99}""");
        }

        env.InnerHandler = new TestHttpMessageHandler().Push(Handle).Push(Handle);

        var exit = await env.Invoke(
            ["issue", "update", "TECH-1", "--summary", "new", "--overwrite-latest"],
            new StringWriter(), new StringWriter());

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(methods[0]).IsEqualTo(HttpMethod.Get);
        await Assert.That(methods[1]).IsEqualTo(HttpMethod.Patch);
        await Assert.That(queries[1]).IsEqualTo("?version=99");
    }

    /// <summary>
    /// <c>--version</c> вместе с <c>--overwrite-latest</c> — ошибка аргументов (exit 2).
    /// </summary>
    [Test]
    public async Task IssueUpdate_VersionWithOverwriteLatest_FailsInvalidArgs()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var er = new StringWriter();
        var exit = await env.Invoke(
            ["issue", "update", "TECH-1", "--summary", "new", "--version", "5", "--overwrite-latest"],
            new StringWriter(), er);

        await Assert.That(exit).IsEqualTo(2);
        await Assert.That(er.ToString()).Contains("invalid_args");
        await Assert.That(er.ToString()).Contains("mutually exclusive");
    }

    /// <summary>
    /// Успешный PATCH запоминает новую версию из ответа: второй <c>update</c> подряд
    /// уходит уже с ней.
    /// </summary>
    [Test]
    public async Task IssueUpdate_RemembersVersionFromPatchResponse()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        await Seed(ResourceVersionFlow.IssueResource, "TECH-1", 7);

        var queries = new List<string>();
        env.InnerHandler = new TestHttpMessageHandler()
            .Push(req => Json(queries, req, """{"key":"TECH-1","version":8}"""))
            .Push(req => Json(queries, req, """{"key":"TECH-1","version":9}"""));

        await env.Invoke(["issue", "update", "TECH-1", "--summary", "a"], new StringWriter(), new StringWriter());
        await env.Invoke(["issue", "update", "TECH-1", "--summary", "b"], new StringWriter(), new StringWriter());

        await Assert.That(queries[0]).IsEqualTo("?version=7");
        await Assert.That(queries[1]).IsEqualTo("?version=8");
    }

    /// <summary>
    /// HTTP 409 на версии из кэша даёт exit 12 и сообщение с возрастом записи и командой
    /// перечитывания.
    /// </summary>
    [Test]
    public async Task IssueUpdate_ConflictOnCachedVersion_ExplainsAgeAndReread()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        await Seed(ResourceVersionFlow.IssueResource, "TECH-1", 7, DateTimeOffset.UtcNow.AddDays(-3));

        env.InnerHandler = new TestHttpMessageHandler()
            .Push(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("""{"errorMessages":["version mismatch"]}""",
                    Encoding.UTF8, "application/json"),
            });

        var er = new StringWriter();
        var exit = await env.Invoke(
            ["issue", "update", "TECH-1", "--summary", "new"], new StringWriter(), er);

        await Assert.That(exit).IsEqualTo(12);
        var stderr = er.ToString();
        await Assert.That(stderr).Contains("version_conflict");
        await Assert.That(stderr).Contains("Версия 7");
        await Assert.That(stderr).Contains("3 дня назад");
        await Assert.That(stderr).Contains("yt issue get TECH-1");
    }

    /// <summary>
    /// Тот же конфликт с явным <c>--version</c> не дополняется: версию назвал пользователь,
    /// пересказывать её ему нечего.
    /// </summary>
    [Test]
    public async Task IssueUpdate_ConflictOnExplicitVersion_IsNotExplained()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        env.InnerHandler = new TestHttpMessageHandler()
            .Push(_ => new HttpResponseMessage(HttpStatusCode.PreconditionFailed));

        var er = new StringWriter();
        var exit = await env.Invoke(
            ["issue", "update", "TECH-1", "--summary", "new", "--version", "5"], new StringWriter(), er);

        await Assert.That(exit).IsEqualTo(12);
        await Assert.That(er.ToString()).DoesNotContain("перечитайте");
    }

    /// <summary>
    /// Битый файл кэша не роняет команду — она ведёт себя как при промахе.
    /// </summary>
    [Test]
    public async Task IssueUpdate_CorruptedCache_FallsBackToNoVersion()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var path = ResourceVersionCache.DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "not json");

        var queries = new List<string>();
        env.InnerHandler = new TestHttpMessageHandler()
            .Push(req => Json(queries, req, """{"key":"TECH-1"}"""));

        var exit = await env.Invoke(
            ["issue", "update", "TECH-1", "--summary", "new"], new StringWriter(), new StringWriter());

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(queries[0]).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// Версия запоминается по профилю: чужой профиль её не видит.
    /// </summary>
    [Test]
    public async Task IssueUpdate_OtherProfile_DoesNotSeeCachedVersion()
    {
        using var env = new TestEnv();
        env.SetConfig(TwoProfileConfig);
        await Seed(ResourceVersionFlow.IssueResource, "TECH-1", 7, profile: "work");

        var queries = new List<string>();
        env.InnerHandler = new TestHttpMessageHandler()
            .Push(req => Json(queries, req, """{"key":"TECH-1"}"""));

        var exit = await env.Invoke(
            ["issue", "update", "TECH-1", "--summary", "new", "--profile", "home"],
            new StringWriter(), new StringWriter());

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(queries[0]).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// <c>automation trigger get</c> → <c>automation trigger update</c>: версия из кэша
    /// закрывает случай, когда пользователь не передал ни флага, ни поля в теле.
    /// </summary>
    [Test]
    public async Task TriggerGetThenUpdate_SendsRememberedVersion()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var queries = new List<string>();
        env.InnerHandler = new TestHttpMessageHandler()
            .Push(req => Json(queries, req, """{"id":17,"version":2}"""))
            .Push(req => Json(queries, req, """{"id":17,"version":3}"""));

        await env.Invoke(
            ["automation", "trigger", "get", "17", "--queue", "DEV"],
            new StringWriter(), new StringWriter());
        var update = await env.Invoke(
            ["automation", "trigger", "update", "17", "--queue", "DEV", "--name", "renamed"],
            new StringWriter(), new StringWriter());

        await Assert.That(update).IsEqualTo(0);
        await Assert.That(queries[1]).IsEqualTo("?version=2");
    }

    /// <summary>
    /// <c>automation autoaction deactivate</c> тоже подставляет запомненную версию.
    /// </summary>
    [Test]
    public async Task AutoactionGetThenDeactivate_SendsRememberedVersion()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var queries = new List<string>();
        env.InnerHandler = new TestHttpMessageHandler()
            .Push(req => Json(queries, req, """{"id":5,"version":11}"""))
            .Push(req => Json(queries, req, """{"id":5,"version":12}"""));

        await env.Invoke(
            ["automation", "autoaction", "get", "5", "--queue", "DEV"],
            new StringWriter(), new StringWriter());
        var deactivate = await env.Invoke(
            ["automation", "autoaction", "deactivate", "5", "--queue", "DEV"],
            new StringWriter(), new StringWriter());

        await Assert.That(deactivate).IsEqualTo(0);
        await Assert.That(queries[1]).IsEqualTo("?version=11");
    }

    /// <summary>
    /// Триггеры и автодействия с одинаковым идентификатором не путаются между собой.
    /// </summary>
    [Test]
    public async Task TriggerAndAutoaction_WithSameId_DoNotShareCacheEntry()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var queries = new List<string>();
        env.InnerHandler = new TestHttpMessageHandler()
            .Push(req => Json(queries, req, """{"id":9,"version":100}"""))
            .Push(req => Json(queries, req, """{"id":9,"version":101}"""));

        await env.Invoke(
            ["automation", "trigger", "get", "9", "--queue", "DEV"],
            new StringWriter(), new StringWriter());
        var deactivate = await env.Invoke(
            ["automation", "autoaction", "deactivate", "9", "--queue", "DEV"],
            new StringWriter(), new StringWriter());

        await Assert.That(deactivate).IsEqualTo(0);
        await Assert.That(queries[1]).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// Организация входит в ключ: имя профиля то же, но <c>YT_ORG_ID</c> перекрыл организацию,
    /// и версия, прочитанная в чужой организации, не подставляется.
    /// </summary>
    [Test]
    public async Task IssueUpdate_OtherOrganization_DoesNotSeeCachedVersion()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        await Seed(ResourceVersionFlow.IssueResource, "TECH-1", 7);
        env.Set("YT_ORG_ID", "other-org");

        var queries = new List<string>();
        env.InnerHandler = new TestHttpMessageHandler()
            .Push(req => Json(queries, req, """{"key":"TECH-1"}"""));

        var exit = await env.Invoke(
            ["issue", "update", "TECH-1", "--summary", "new"], new StringWriter(), new StringWriter());

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(queries[0]).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// Тип организации тоже разделяет записи: тот же профиль и тот же <c>org_id</c>,
    /// но <c>YT_ORG_TYPE=yandex360</c> — это другая организация.
    /// </summary>
    [Test]
    public async Task IssueUpdate_OtherOrgType_DoesNotSeeCachedVersion()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        await Seed(ResourceVersionFlow.IssueResource, "TECH-1", 7);
        env.Set("YT_ORG_TYPE", "yandex360");

        var queries = new List<string>();
        env.InnerHandler = new TestHttpMessageHandler()
            .Push(req => Json(queries, req, """{"key":"TECH-1"}"""));

        var exit = await env.Invoke(
            ["issue", "update", "TECH-1", "--summary", "new"], new StringWriter(), new StringWriter());

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(queries[0]).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// Организация из окружения не сваливается в чужую запись и на записи: версию,
    /// запомненную под <c>YT_ORG_ID</c>, видит только тот же самый оверрайд.
    /// </summary>
    [Test]
    public async Task IssueGetThenUpdate_UnderOrgOverride_SharesOwnCacheEntry()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);
        env.Set("YT_ORG_ID", "other-org");

        var queries = new List<string>();
        env.InnerHandler = new TestHttpMessageHandler()
            .Push(req => Json(queries, req, """{"key":"TECH-1","version":7}"""))
            .Push(req => Json(queries, req, """{"key":"TECH-1","version":8}"""));

        await env.Invoke(["issue", "get", "TECH-1"], new StringWriter(), new StringWriter());
        var update = await env.Invoke(
            ["issue", "update", "TECH-1", "--summary", "new"], new StringWriter(), new StringWriter());

        await Assert.That(update).IsEqualTo(0);
        await Assert.That(queries[1]).IsEqualTo("?version=7");
    }

    /// <summary>
    /// <c>--overwrite-latest</c>, не увидев версии в ответе GET, отказывается отправлять
    /// мутацию: запись без версии — это запись вслепую, ровно то, чего флаг просил избежать.
    /// </summary>
    [Test]
    public async Task IssueUpdate_OverwriteLatest_WithoutVersionInResponse_DoesNotMutate()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var handler = new TestHttpMessageHandler();
        var queries = new List<string>();
        handler.Push(req => Json(queries, req, """{"key":"TECH-1"}"""));
        env.InnerHandler = handler;

        var er = new StringWriter();
        var exit = await env.Invoke(
            ["issue", "update", "TECH-1", "--summary", "new", "--overwrite-latest"],
            new StringWriter(), er);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(handler.Seen.Count).IsEqualTo(1);
        await Assert.That(handler.Seen[0].Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(er.ToString()).Contains("unexpected");
        await Assert.That(er.ToString()).Contains("--overwrite-latest");
    }

    /// <summary>
    /// Ключ задачи регистронезависим: <c>get tech-1</c> и <c>update TECH-1</c> — один ресурс
    /// и одна запись кэша.
    /// </summary>
    [Test]
    public async Task IssueGetThenUpdate_MatchesIssueKeyIgnoringCase()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var queries = new List<string>();
        env.InnerHandler = new TestHttpMessageHandler()
            .Push(req => Json(queries, req, """{"key":"TECH-1","version":7}"""))
            .Push(req => Json(queries, req, """{"key":"TECH-1","version":8}"""));

        await env.Invoke(["issue", "get", "tech-1"], new StringWriter(), new StringWriter());
        var update = await env.Invoke(
            ["issue", "update", "TECH-1", "--summary", "new"], new StringWriter(), new StringWriter());

        await Assert.That(update).IsEqualTo(0);
        await Assert.That(queries[1]).IsEqualTo("?version=7");
    }

    /// <summary>
    /// Ключ очереди в составном идентификаторе автоматизации тоже регистронезависим.
    /// </summary>
    [Test]
    public async Task TriggerGetThenUpdate_MatchesQueueKeyIgnoringCase()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var queries = new List<string>();
        env.InnerHandler = new TestHttpMessageHandler()
            .Push(req => Json(queries, req, """{"id":17,"version":2}"""))
            .Push(req => Json(queries, req, """{"id":17,"version":3}"""));

        await env.Invoke(
            ["automation", "trigger", "get", "17", "--queue", "dev"],
            new StringWriter(), new StringWriter());
        var update = await env.Invoke(
            ["automation", "trigger", "update", "17", "--queue", "DEV", "--name", "renamed"],
            new StringWriter(), new StringWriter());

        await Assert.That(update).IsEqualTo(0);
        await Assert.That(queries[1]).IsEqualTo("?version=2");
    }

    private const string TwoProfileConfig =
        """{"default_profile":"work","profiles":{"work":{"org_type":"cloud","org_id":"o","auth":{"type":"oauth","token":"y0_X"}},"home":{"org_type":"cloud","org_id":"o2","auth":{"type":"oauth","token":"y0_Y"}}}}""";

    /// <summary>
    /// Кладёт запись прямо в файл кэша, минуя команды: тест задаёт исходное состояние.
    /// </summary>
    /// <param name="resourceType">Дискриминатор типа ресурса.</param>
    /// <param name="resourceId">Идентификатор ресурса.</param>
    /// <param name="version">Версия.</param>
    /// <param name="readAt">Момент чтения; <c>null</c> — сейчас.</param>
    /// <param name="profile">Имя профиля; по умолчанию <c>default</c>.</param>
    /// <param name="orgId">Идентификатор организации; по умолчанию тот, что в <see cref="TestEnv.MinimalOAuthConfig"/>.</param>
    /// <returns>Задача, завершающаяся после записи.</returns>
    private static Task Seed(
        string resourceType,
        string resourceId,
        long version,
        DateTimeOffset? readAt = null,
        string profile = "default",
        string orgId = "o")
    {
        var cache = new ResourceVersionCache(ResourceVersionCache.DefaultPath);
        var effective = new EffectiveProfile(
            profile, OrgType.Cloud, orgId, ReadOnly: false,
            new AuthConfig(AuthType.OAuth, Token: "y0_X"), ExternalEffectsAllowed: true);
        return cache.Set(
            ResourceVersionCache.BuildKey(effective, resourceType, resourceId), version, readAt);
    }

    /// <summary>
    /// Записывает query запроса и отдаёт JSON-ответ 200.
    /// </summary>
    /// <param name="queries">Аккумулятор query-строк по порядку запросов.</param>
    /// <param name="req">Входящий запрос.</param>
    /// <param name="payload">Тело ответа.</param>
    /// <returns>Ответ 200 с указанным телом.</returns>
    private static HttpResponseMessage Json(List<string> queries, HttpRequestMessage req, string payload)
    {
        queries.Add(req.RequestUri!.Query);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
    }
}
