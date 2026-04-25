namespace YandexTrackerCLI.Tests.Commands.Issue;

using TUnit.Core;
using YandexTrackerCLI.Commands.Issue;
using Core.Api.Errors;

/// <summary>
/// Unit-тесты чистого транслятора <see cref="IssueFilterBuilder"/>:
/// без HTTP, без глобального state, поэтому параллелизм допустим.
/// </summary>
public sealed class IssueFilterBuilderTests
{
    /// <summary>
    /// Если задан только <c>--yql</c>, билдер возвращает его без изменений.
    /// </summary>
    [Test]
    public async Task Build_YqlOnly_ReturnsAsIs()
    {
        var f = new IssueFilters(
            Yql: "Queue: DEV",
            Queue: null,
            Status: null,
            Assignee: null,
            Type: null,
            Priority: null,
            UpdatedSince: null,
            CreatedSince: null,
            Text: null,
            Tag: null);

        var yql = IssueFilterBuilder.Build(f);

        await Assert.That(yql).IsEqualTo("Queue: DEV");
    }

    /// <summary>
    /// Одиночный <c>--queue</c> → <c>Queue: "VAL"</c>.
    /// </summary>
    [Test]
    public async Task Build_QueueOnly()
    {
        var yql = IssueFilterBuilder.Build(new IssueFilters(
            Yql: null,
            Queue: "DEV",
            Status: null,
            Assignee: null,
            Type: null,
            Priority: null,
            UpdatedSince: null,
            CreatedSince: null,
            Text: null,
            Tag: null));

        await Assert.That(yql).IsEqualTo("Queue: \"DEV\"");
    }

    /// <summary>
    /// Несколько simple-фильтров соединяются через <c> AND </c>.
    /// </summary>
    [Test]
    public async Task Build_QueueAndStatus_JoinedByAnd()
    {
        var yql = IssueFilterBuilder.Build(new IssueFilters(
            Yql: null,
            Queue: "DEV",
            Status: "open",
            Assignee: null,
            Type: null,
            Priority: null,
            UpdatedSince: null,
            CreatedSince: null,
            Text: null,
            Tag: null));

        await Assert.That(yql).IsEqualTo("Queue: \"DEV\" AND Status: \"open\"");
    }

    /// <summary>
    /// Значение CSV для множественного фильтра превращается в YQL-список.
    /// </summary>
    [Test]
    public async Task Build_StatusMultiple_AsList()
    {
        var yql = IssueFilterBuilder.Build(new IssueFilters(
            Yql: null,
            Queue: null,
            Status: "open,pending",
            Assignee: null,
            Type: null,
            Priority: null,
            UpdatedSince: null,
            CreatedSince: null,
            Text: null,
            Tag: null));

        await Assert.That(yql).IsEqualTo("Status: (\"open\", \"pending\")");
    }

    /// <summary>
    /// Специальное значение <c>me</c> транслируется в YQL-функцию <c>me()</c>.
    /// </summary>
    [Test]
    public async Task Build_Assignee_Me_UsesFunction()
    {
        var yql = IssueFilterBuilder.Build(new IssueFilters(
            Yql: null,
            Queue: null,
            Status: null,
            Assignee: "me",
            Type: null,
            Priority: null,
            UpdatedSince: null,
            CreatedSince: null,
            Text: null,
            Tag: null));

        await Assert.That(yql).IsEqualTo("Assignee: me()");
    }

    /// <summary>
    /// Обычный логин для <c>--assignee</c> оборачивается в строковый литерал.
    /// </summary>
    [Test]
    public async Task Build_Assignee_Login_IsQuoted()
    {
        var yql = IssueFilterBuilder.Build(new IssueFilters(
            Yql: null,
            Queue: null,
            Status: null,
            Assignee: "user1",
            Type: null,
            Priority: null,
            UpdatedSince: null,
            CreatedSince: null,
            Text: null,
            Tag: null));

        await Assert.That(yql).IsEqualTo("Assignee: \"user1\"");
    }

    /// <summary>
    /// <c>--text</c> порождает OR по двум полям: Summary и Description.
    /// </summary>
    [Test]
    public async Task Build_Text_BothFields_Or()
    {
        var yql = IssueFilterBuilder.Build(new IssueFilters(
            Yql: null,
            Queue: null,
            Status: null,
            Assignee: null,
            Type: null,
            Priority: null,
            UpdatedSince: null,
            CreatedSince: null,
            Text: "fix",
            Tag: null));

        await Assert.That(yql).IsEqualTo("(Summary: \"fix\" OR Description: \"fix\")");
    }

    /// <summary>
    /// В значениях двойные кавычки экранируются как <c>\"</c>, а обратные слэши как <c>\\</c>.
    /// </summary>
    [Test]
    public async Task Build_EscapesQuotesAndBackslashes()
    {
        var yql = IssueFilterBuilder.Build(new IssueFilters(
            Yql: null,
            Queue: "odd\"Q\\",
            Status: null,
            Assignee: null,
            Type: null,
            Priority: null,
            UpdatedSince: null,
            CreatedSince: null,
            Text: null,
            Tag: null));

        await Assert.That(yql).IsEqualTo("Queue: \"odd\\\"Q\\\\\"");
    }

    /// <summary>
    /// Одновременное использование <c>--yql</c> и simple-фильтра запрещено.
    /// </summary>
    [Test]
    public async Task Build_YqlAndSimple_Throws()
    {
        var ex = Assert.Throws<TrackerException>(() => IssueFilterBuilder.Build(new IssueFilters(
            Yql: "x",
            Queue: "DEV",
            Status: null,
            Assignee: null,
            Type: null,
            Priority: null,
            UpdatedSince: null,
            CreatedSince: null,
            Text: null,
            Tag: null)));

        await Assert.That(ex.Code).IsEqualTo(ErrorCode.InvalidArgs);
    }

    /// <summary>
    /// Отсутствие любых фильтров — <c>InvalidArgs</c>.
    /// </summary>
    [Test]
    public async Task Build_Nothing_Throws()
    {
        var ex = Assert.Throws<TrackerException>(() => IssueFilterBuilder.Build(new IssueFilters(
            Yql: null,
            Queue: null,
            Status: null,
            Assignee: null,
            Type: null,
            Priority: null,
            UpdatedSince: null,
            CreatedSince: null,
            Text: null,
            Tag: null)));

        await Assert.That(ex.Code).IsEqualTo(ErrorCode.InvalidArgs);
    }

    /// <summary>
    /// Управляющие символы (в т.ч. CR/LF) внутри значений отвергаются.
    /// </summary>
    [Test]
    public async Task Build_ControlCharInValue_Throws()
    {
        var ex = Assert.Throws<TrackerException>(() => IssueFilterBuilder.Build(new IssueFilters(
            Yql: null,
            Queue: "a\rb",
            Status: null,
            Assignee: null,
            Type: null,
            Priority: null,
            UpdatedSince: null,
            CreatedSince: null,
            Text: null,
            Tag: null)));

        await Assert.That(ex.Code).IsEqualTo(ErrorCode.InvalidArgs);
    }

    /// <summary>
    /// Невалидная дата в <c>--updated-since</c> → <c>InvalidArgs</c>.
    /// </summary>
    [Test]
    public async Task Build_InvalidDate_Throws()
    {
        var ex = Assert.Throws<TrackerException>(() => IssueFilterBuilder.Build(new IssueFilters(
            Yql: null,
            Queue: null,
            Status: null,
            Assignee: null,
            Type: null,
            Priority: null,
            UpdatedSince: "nope",
            CreatedSince: null,
            Text: null,
            Tag: null)));

        await Assert.That(ex.Code).IsEqualTo(ErrorCode.InvalidArgs);
    }
}
