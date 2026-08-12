namespace YandexTrackerCLI.Core.Tests.Config;

using TUnit.Core;
using YandexTrackerCLI.Core.Api.Errors;
using YandexTrackerCLI.Core.Config;

/// <summary>
/// Тесты нормализации и проверки политики <c>allowed_queues</c>.
/// </summary>
public sealed class QueuePolicyTests
{
    [Test]
    public async Task ParseList_TrimsDropsEmptyAndDeduplicates()
    {
        var parsed = QueuePolicy.ParseList(" DEV , ,OPS,  dev ,QA,");

        await Assert.That(parsed).IsEquivalentTo(new[] { "DEV", "OPS", "QA" });
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments(",, ,")]
    public async Task ParseList_EmptyInput_MeansNoRestriction(string value)
    {
        var parsed = QueuePolicy.ParseList(value);

        await Assert.That(parsed).IsEmpty();
        await Assert.That(QueuePolicy.IsRestricted(parsed)).IsFalse();
    }

    [Test]
    public async Task FormatList_JoinsWithComma_AndRoundTripsThroughParse()
    {
        var formatted = QueuePolicy.FormatList(new[] { "DEV", "QA" });

        await Assert.That(formatted).IsEqualTo("DEV,QA");
        await Assert.That(QueuePolicy.ParseList(formatted)).IsEquivalentTo(new[] { "DEV", "QA" });
    }

    [Test]
    public async Task IsAllowed_EmptyList_AllowsEverything()
    {
        await Assert.That(QueuePolicy.IsAllowed(Array.Empty<string>(), "ANY")).IsTrue();
        await Assert.That(QueuePolicy.IsAllowed(null, "ANY")).IsTrue();
    }

    [Test]
    [Arguments("DEV", true)]
    [Arguments("dev", true)]
    [Arguments("OPS", false)]
    public async Task IsAllowed_ComparesCaseInsensitively(string queue, bool expected)
    {
        await Assert.That(QueuePolicy.IsAllowed(new[] { "DEV", "QA" }, queue)).IsEqualTo(expected);
    }

    [Test]
    public async Task IsAllowed_Restricted_UnknownQueue_IsDenied()
    {
        await Assert.That(QueuePolicy.IsAllowed(new[] { "DEV" }, null)).IsFalse();
        await Assert.That(QueuePolicy.IsAllowed(new[] { "DEV" }, "  ")).IsFalse();
    }

    [Test]
    [Arguments("DEV-1", "DEV")]
    [Arguments("DEV-1-2", "DEV")]
    [Arguments("OPS", "OPS")]
    public async Task QueueOfIssueKey_TakesPrefixBeforeFirstDash(string key, string expected)
    {
        await Assert.That(QueuePolicy.QueueOfIssueKey(key)).IsEqualTo(expected);
    }

    [Test]
    public async Task Denied_CarriesPolicyViolation_AndNamesQueueProfileAndList()
    {
        var ex = QueuePolicy.Denied("OPS", "ci", new[] { "DEV", "QA" });

        await Assert.That(ex.Code).IsEqualTo(ErrorCode.PolicyViolation);
        await Assert.That(ex.Message)
            .IsEqualTo("queue 'OPS' is outside allowed_queues of profile 'ci' (allowed: DEV, QA)");
    }
}
