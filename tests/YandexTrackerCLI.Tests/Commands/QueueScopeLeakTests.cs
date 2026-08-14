namespace YandexTrackerCLI.Tests.Commands;

using System.Net;
using System.Text;
using System.Text.Json;
using TUnit.Core;
using Http;
using YandexTrackerCLI.Commands.Suggest;

/// <summary>
/// Содержательные тесты для команд из <see cref="QueueScopeInventory.LeaksIssueCollections"/>:
/// стаб отдаёт ответ той формы, которую ждёт код команды, в ответе есть и разрешённая
/// задача (очередь), и чужая; проверяется фактический текст stdout.
/// </summary>
/// <remarks>
/// <para>
/// Проверяется именно stdout как текст, а не результат его разбора: команда может печатать
/// сырое тело ответа, и разбор в тесте скрыл бы утечку. Stderr не проверяется намеренно —
/// сообщение об отказе по политике обязано называть запрещённую очередь.
/// </para>
/// <para>
/// Мутируют глобальный state (env + Console + AsyncLocal), поэтому последовательно.
/// </para>
/// </remarks>
[NotInParallel("yt-cli-global-state")]
public sealed class QueueScopeLeakTests
{
    private const string RestrictedConfig =
        """
        {
          "default_profile":"ci",
          "profiles":{"ci":{"org_type":"cloud","org_id":"o","read_only":false,
                            "allowed_queues":["DEV","QA"],
                            "auth":{"type":"oauth","token":"y0_X"}}}
        }
        """;

    /// <summary>
    /// Команды первой группы, которые нельзя прогнать end-to-end, — с причиной и указанием
    /// теста, который покрывает их вместо этого.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> CoveredWithoutEndToEnd =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["suggest"] =
                "TTY-only: при перенаправлённом stdout команда отказывает до похода в сеть, "
                + "поэтому e2e-прогон был бы пустым. Покрыта "
                + nameof(Suggest_DropsForeignIssuesFromPickerItems) + ".",
        };

    /// <summary>
    /// Описание одного содержательного прогона.
    /// </summary>
    /// <param name="Path">Путь листовой команды, как в <see cref="QueueScopeInventory"/>.</param>
    /// <param name="Name">Человекочитаемое имя случая (попадает в имя теста).</param>
    /// <param name="Args">Аргументы запуска CLI.</param>
    /// <param name="Responses">Ответы стаба в порядке FIFO.</param>
    /// <param name="ExpectedExit">Ожидаемый exit-код.</param>
    /// <param name="MustNotAppearInStdout">Подстроки, которых в stdout быть не должно.</param>
    /// <param name="MustAppearInStdout">Подстроки, которые в stdout быть обязаны.</param>
    public sealed record LeakCase(
        string Path,
        string Name,
        string[] Args,
        Func<HttpResponseMessage>[] Responses,
        int ExpectedExit,
        string[] MustNotAppearInStdout,
        string[] MustAppearInStdout)
    {
        /// <inheritdoc />
        public override string ToString() => Name;
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Page(string body, int totalPages = 1)
    {
        var r = Json(body);
        r.Headers.Add("X-Total-Pages", totalPages.ToString());
        return r;
    }

    // Чужая задача и чужая очередь помечены так, чтобы их присутствие ловилось по подстроке
    // в любом формате вывода: ни "OPS", ни "secret" не встречаются в разрешённых данных.
    private const string ForeignIssueBody =
        """[{"key":"DEV-1","summary":"ok"},{"key":"OPS-7","summary":"secret"},{"key":"QA-2","summary":"ok2"}]""";

    private const string ForeignQueueBody =
        """[{"key":"DEV","name":"ok"},{"key":"OPS","name":"secret"},{"key":"QA","name":"ok2"}]""";

    private const string ForeignLinksBody =
        """
        [{"id":1,"object":{"key":"QA-2","display":"ok"}},
         {"id":2,"object":{"key":"OPS-7","display":"secret"}}]
        """;

    private const string ForeignOwnedResourceBody =
        """{"id":"7","name":"secret","queue":{"key":"OPS","display":"secret queue"}}""";

    private static readonly string[] NoForeignTrace = ["OPS", "secret"];

    /// <summary>
    /// Таблица прогонов. Ключ каждой записи — путь листовой команды; связь с
    /// <see cref="QueueScopeInventory.LeaksIssueCollections"/> проверяет
    /// <see cref="EveryFilteringCommand_HasContentTest"/>.
    /// </summary>
    /// <returns>Случаи для параметризованного теста.</returns>
    public static IEnumerable<Func<LeakCase>> Cases()
    {
        yield return () => new LeakCase(
            "issue find",
            "issue find (агрегированный вывод)",
            ["issue", "find", "--yql", "Updated: today()"],
            [() => Page(ForeignIssueBody)],
            0,
            NoForeignTrace,
            ["DEV-1", "QA-2"]);

        yield return () => new LeakCase(
            "issue find",
            "issue find --stream (NDJSON)",
            ["issue", "find", "--yql", "Updated: today()", "--stream"],
            [() => Page(ForeignIssueBody)],
            0,
            NoForeignTrace,
            ["DEV-1", "QA-2"]);

        yield return () => new LeakCase(
            "queue list",
            "queue list",
            ["queue", "list"],
            [() => Page(ForeignQueueBody)],
            0,
            NoForeignTrace,
            ["DEV", "QA"]);

        yield return () => new LeakCase(
            "link list",
            "link list DEV-1",
            ["link", "list", "DEV-1"],
            [() => Json(ForeignLinksBody)],
            0,
            NoForeignTrace,
            ["QA-2"]);

        foreach (var group in new[] { "component", "version" })
        {
            yield return () => new LeakCase(
                $"{group} get",
                $"{group} get 7 (чужая очередь-владелец)",
                [group, "get", "7"],
                [() => Json(ForeignOwnedResourceBody)],
                10,
                NoForeignTrace,
                []);

            yield return () => new LeakCase(
                $"{group} update",
                $"{group} update 7 (чужая очередь-владелец)",
                [group, "update", "7", "--name", "n2"],
                [() => Json(ForeignOwnedResourceBody)],
                10,
                NoForeignTrace,
                []);

            yield return () => new LeakCase(
                $"{group} delete",
                $"{group} delete 7 (чужая очередь-владелец)",
                [group, "delete", "7"],
                [() => Json(ForeignOwnedResourceBody)],
                10,
                NoForeignTrace,
                []);
        }
    }

    /// <summary>
    /// Ни одна структурная ссылка на чужую очередь не доходит до stdout, а разрешённая — доходит.
    /// </summary>
    /// <param name="testCase">Описание прогона.</param>
    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task ForeignQueueEntities_NeverReachStdout(LeakCase testCase)
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);

        var inner = new TestHttpMessageHandler();
        foreach (var response in testCase.Responses)
        {
            inner.Push(_ => response());
        }
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(testCase.Args, sw, er);
        var stdout = sw.ToString();

        await Assert.That(exit).IsEqualTo(testCase.ExpectedExit);

        foreach (var forbidden in testCase.MustNotAppearInStdout)
        {
            await Assert.That(stdout).DoesNotContain(forbidden);
        }

        foreach (var required in testCase.MustAppearInStdout)
        {
            await Assert.That(stdout).Contains(required);
        }
    }

    /// <summary>
    /// Регрессия к обратному: без ограничения профиля те же команды печатают всё, что пришло, —
    /// иначе «утечки нет» доказывалось бы просто пустым выводом.
    /// </summary>
    [Test]
    public async Task Unrestricted_StillPrintsEverything()
    {
        using var env = new TestEnv();
        env.SetConfig(TestEnv.MinimalOAuthConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Page(ForeignIssueBody));
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(["issue", "find", "--yql", "Updated: today()"], sw, er);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(sw.ToString()).Contains("OPS-7");
    }

    /// <summary>
    /// <c>suggest</c> прогнать e2e нельзя (TTY-only), поэтому проверяется набор элементов,
    /// который уходит в пикер: сериализованные элементы не содержат ни ключа чужой задачи,
    /// ни её темы.
    /// </summary>
    [Test]
    public async Task Suggest_DropsForeignIssuesFromPickerItems()
    {
        using var doc = JsonDocument.Parse(ForeignIssueBody);

        var shown = SuggestCommand.FilterSuggestions(doc.RootElement, 10, ["DEV", "QA"]);
        var rendered = string.Join("\n", shown.Select(e => e.GetRawText()));

        await Assert.That(rendered).DoesNotContain("OPS");
        await Assert.That(rendered).DoesNotContain("secret");
        await Assert.That(rendered).Contains("DEV-1");
        await Assert.That(rendered).Contains("QA-2");
    }

    /// <summary>
    /// Фиксирует задокументированный предел вместо того, чтобы делать вид, что его нет:
    /// <c>issue get</c> печатает ответ как есть, поэтому поле <c>parent</c> разрешённой задачи
    /// показывает и ключ чужой задачи, и её <c>display</c> — то есть тему, а не только ключ.
    /// </summary>
    /// <remarks>
    /// Тест сознательно закрепляет текущее поведение (characterization): проверять здесь
    /// «темы чужой задачи в выводе нет» было бы неверно — её там видно. Если фильтрацию
    /// полей связей когда-нибудь введут, тест упадёт и заставит переписать причину в
    /// <see cref="QueueScopeInventory.NoIssueCollections"/>, а не молча разойтись с ней.
    /// Читать чужую задачу это по-прежнему не позволяет — вторая половина теста.
    /// </remarks>
    [Test]
    public async Task DocumentedLimit_IssueGet_ShowsForeignKeyAndDisplayOfParent()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);

        var inner = new TestHttpMessageHandler();
        inner.Push(_ => Json(
            """{"key":"DEV-1","parent":{"key":"OPS-7","display":"secret"}}"""));
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(["issue", "get", "DEV-1"], sw, er);

        await Assert.That(exit).IsEqualTo(0);
        // Предел: ключ и тема родителя из чужой очереди видны.
        await Assert.That(sw.ToString()).Contains("OPS-7");
        await Assert.That(sw.ToString()).Contains("secret");

        // Но сама чужая задача остаётся нечитаемой — запрос по её ключу не уходит вовсе.
        var sw2 = new StringWriter();
        var denied = await env.Invoke(["issue", "get", "OPS-7"], sw2, er);
        await Assert.That(denied).IsEqualTo(10);
        await Assert.That(inner.Seen.Count).IsEqualTo(1);
    }

    /// <summary>
    /// Уточняет предел «справочники не фильтруются»: глобальные поля действительно отдаются
    /// как есть, но локальные поля чужой очереди адресуются через
    /// <c>queues/{queue}/localFields</c> и потому отклоняются HTTP-guard'ом до выхода в сеть.
    /// </summary>
    [Test]
    public async Task FieldList_LocalFieldsOfForeignQueue_IsBlocked()
    {
        using var env = new TestEnv();
        env.SetConfig(RestrictedConfig);
        var inner = new TestHttpMessageHandler();
        env.InnerHandler = inner;

        var sw = new StringWriter();
        var er = new StringWriter();
        var exit = await env.Invoke(["field", "list", "--queue", "OPS"], sw, er);

        await Assert.That(exit).IsEqualTo(10);
        await Assert.That(inner.Seen).IsEmpty();
        await Assert.That(sw.ToString()).IsEmpty();
    }

    /// <summary>
    /// Каждая команда первой группы покрыта либо прогоном из <see cref="Cases"/>, либо
    /// отдельным тестом с записанной причиной, почему e2e невозможен. Обратное тоже
    /// проверяется: прогон для команды, которой в первой группе нет, — ошибка классификации.
    /// </summary>
    [Test]
    public async Task EveryFilteringCommand_HasContentTest()
    {
        var covered = Cases().Select(c => c().Path)
            .Concat(CoveredWithoutEndToEnd.Keys)
            .ToHashSet(StringComparer.Ordinal);

        var uncovered = QueueScopeInventory.LeaksIssueCollections
            .Where(p => !covered.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        var extra = covered
            .Where(p => !QueueScopeInventory.LeaksIssueCollections.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        var report = new List<string>();
        if (uncovered.Length > 0)
        {
            report.Add(
                "команды объявлены фильтрующими, но содержательного теста у них нет: "
                + string.Join(", ", uncovered));
        }
        if (extra.Length > 0)
        {
            report.Add(
                "содержательный тест есть, а в QueueScopeInventory.LeaksIssueCollections команды нет: "
                + string.Join(", ", extra));
        }

        await Assert.That(string.Join("; ", report)).IsEmpty();
    }
}
