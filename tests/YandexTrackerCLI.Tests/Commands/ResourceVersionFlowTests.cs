namespace YandexTrackerCLI.Tests.Commands;

using TUnit.Core;
using YandexTrackerCLI.Commands;
using YandexTrackerCLI.Core.Api.Errors;

/// <summary>
/// Юнит-тесты <see cref="ResourceVersionFlow"/>: описание возраста записи по-русски
/// и правила, по которым ошибка конфликта дополняется контекстом.
/// </summary>
public sealed class ResourceVersionFlowTests
{
    /// <summary>
    /// Возраст записи описывается склонённой русской фразой.
    /// </summary>
    /// <param name="seconds">Возраст записи в секундах.</param>
    /// <param name="expected">Ожидаемая фраза.</param>
    [Test]
    [Arguments(5, "только что")]
    [Arguments(59, "только что")]
    [Arguments(60, "1 минуту назад")]
    [Arguments(120, "2 минуты назад")]
    [Arguments(300, "5 минут назад")]
    [Arguments(3600, "1 час назад")]
    [Arguments(7200, "2 часа назад")]
    [Arguments(18000, "5 часов назад")]
    [Arguments(86400, "1 день назад")]
    [Arguments(259200, "3 дня назад")]
    [Arguments(950400, "11 дней назад")]
    [Arguments(1814400, "21 день назад")]
    public async Task DescribeAge_UsesRussianPlurals(int seconds, string expected)
    {
        await Assert.That(ResourceVersionFlow.DescribeAge(TimeSpan.FromSeconds(seconds))).IsEqualTo(expected);
    }

    /// <summary>
    /// Отрицательный возраст (часы уехали назад) описывается как «только что».
    /// </summary>
    [Test]
    public async Task DescribeAge_NegativeAge_ReadsAsJustNow()
    {
        await Assert.That(ResourceVersionFlow.DescribeAge(TimeSpan.FromHours(-2))).IsEqualTo("только что");
    }

    /// <summary>
    /// Конфликт по версии из кэша дополняется возрастом записи и командой перечитывания.
    /// </summary>
    [Test]
    public async Task Explain_CacheSourcedConflict_AddsAgeAndCommand()
    {
        var now = DateTimeOffset.UtcNow;
        var decision = new ResourceVersionDecision(
            7, ResourceVersionSource.Cache, now.AddDays(-3));
        var original = new TrackerException(ErrorCode.VersionConflict, "Tracker API returned HTTP 409.", 409);

        var explained = ResourceVersionFlow.Explain(original, decision, "yt issue get TECH-1", now);

        await Assert.That(explained.Code).IsEqualTo(ErrorCode.VersionConflict);
        await Assert.That(explained.HttpStatus).IsEqualTo(409);
        await Assert.That(explained.Message).Contains("Версия 7");
        await Assert.That(explained.Message).Contains("3 дня назад");
        await Assert.That(explained.Message).Contains("yt issue get TECH-1");
    }

    /// <summary>
    /// Ошибки не про конфликт версии остаются нетронутыми.
    /// </summary>
    [Test]
    public async Task Explain_NonConflictError_ReturnsOriginal()
    {
        var decision = new ResourceVersionDecision(7, ResourceVersionSource.Cache, DateTimeOffset.UtcNow);
        var original = new TrackerException(ErrorCode.NotFound, "Tracker API returned HTTP 404.", 404);

        await Assert.That(ResourceVersionFlow.Explain(original, decision, "yt issue get TECH-1"))
            .IsSameReferenceAs(original);
    }

    /// <summary>
    /// Конфликт по версии, которую назвал сам пользователь, не пересказывается.
    /// </summary>
    /// <param name="source">Источник версии.</param>
    [Test]
    [Arguments(ResourceVersionSource.Explicit)]
    [Arguments(ResourceVersionSource.Body)]
    [Arguments(ResourceVersionSource.OverwriteLatest)]
    [Arguments(ResourceVersionSource.None)]
    public async Task Explain_NonCacheSource_ReturnsOriginal(ResourceVersionSource source)
    {
        var decision = new ResourceVersionDecision(7, source, DateTimeOffset.UtcNow);
        var original = new TrackerException(ErrorCode.VersionConflict, "Tracker API returned HTTP 409.", 409);

        await Assert.That(ResourceVersionFlow.Explain(original, decision, "yt issue get TECH-1"))
            .IsSameReferenceAs(original);
    }

    /// <summary>
    /// <c>--version</c> вместе с <c>--overwrite-latest</c> — <see cref="ErrorCode.InvalidArgs"/>.
    /// </summary>
    [Test]
    public async Task EnsureFlagsCompatible_BothSet_Throws()
    {
        var ex = Assert.Throws<TrackerException>(
            () => ResourceVersionFlow.EnsureFlagsCompatible(5, overwriteLatest: true));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.InvalidArgs);
    }

    /// <summary>
    /// По отдельности флаги допустимы.
    /// </summary>
    /// <param name="version">Значение <c>--version</c>.</param>
    /// <param name="overwriteLatest">Значение <c>--overwrite-latest</c>.</param>
    [Test]
    [Arguments(null, false)]
    [Arguments(5L, false)]
    [Arguments(null, true)]
    public async Task EnsureFlagsCompatible_SingleFlag_DoesNotThrow(long? version, bool overwriteLatest)
    {
        var thrown = Record(() => ResourceVersionFlow.EnsureFlagsCompatible(version, overwriteLatest));

        await Assert.That(thrown).IsNull();
    }

    /// <summary>
    /// Ключ задачи регистронезависим и попадает в ключ кэша в верхнем регистре.
    /// </summary>
    /// <param name="input">Ключ, как его ввёл пользователь.</param>
    [Test]
    [Arguments("tech-1")]
    [Arguments("TECH-1")]
    [Arguments("Tech-1")]
    public async Task NormalizeResourceId_UpperCasesIssueKey(string input)
    {
        await Assert.That(
                ResourceVersionFlow.NormalizeResourceId(ResourceVersionFlow.IssueResource, input))
            .IsEqualTo("TECH-1");
    }

    /// <summary>
    /// У автоматизаций к верхнему регистру приводится только ключ очереди — идентификатор
    /// после слэша остаётся как есть.
    /// </summary>
    /// <param name="resourceType">Дискриминатор типа ресурса.</param>
    [Test]
    [Arguments(ResourceVersionFlow.TriggerResource)]
    [Arguments(ResourceVersionFlow.AutoactionResource)]
    public async Task NormalizeResourceId_UpperCasesOnlyQueuePart(string resourceType)
    {
        await Assert.That(ResourceVersionFlow.NormalizeResourceId(resourceType, "dev/17"))
            .IsEqualTo("DEV/17");
    }

    /// <summary>
    /// Незнакомый тип ресурса не нормализуется: склеить два разных ресурса в одну запись
    /// хуже, чем промахнуться мимо кэша.
    /// </summary>
    [Test]
    public async Task NormalizeResourceId_UnknownResourceType_IsLeftAsIs()
    {
        await Assert.That(ResourceVersionFlow.NormalizeResourceId("component", "aBc"))
            .IsEqualTo("aBc");
    }

    /// <summary>
    /// Выполняет действие и возвращает выброшенное исключение либо <c>null</c>.
    /// </summary>
    /// <param name="action">Проверяемое действие.</param>
    /// <returns>Исключение или <c>null</c>.</returns>
    private static Exception? Record(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
