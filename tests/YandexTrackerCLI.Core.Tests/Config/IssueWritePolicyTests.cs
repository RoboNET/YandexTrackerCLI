namespace YandexTrackerCLI.Core.Tests.Config;

using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Core.Api.Errors;
using YandexTrackerCLI.Core.Config;

/// <summary>
/// Тесты политики <c>allowed_write_issues</c>: разбор списка, сравнение ключей,
/// сообщения об отказе и поиск полей рассылки уведомлений в теле запроса.
/// </summary>
public sealed class IssueWritePolicyTests
{
    [Test]
    public async Task ParseList_TrimsDropsEmptyAndDeduplicatesIgnoringCase()
    {
        var parsed = IssueWritePolicy.ParseList(" DEV-42 , , dev-42 ,DEV-43 ");

        await Assert.That(parsed).IsEquivalentTo(new[] { "DEV-42", "DEV-43" });
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments(null)]
    public async Task ParseList_EmptyValue_MeansNoRestriction(string? value)
    {
        var parsed = IssueWritePolicy.ParseList(value);

        await Assert.That(parsed).IsEmpty();
        await Assert.That(IssueWritePolicy.IsRestricted(parsed)).IsFalse();
    }

    [Test]
    public async Task FormatList_JoinsWithComma()
    {
        await Assert.That(IssueWritePolicy.FormatList(new[] { "DEV-42", "DEV-43" }))
            .IsEqualTo("DEV-42,DEV-43");
        await Assert.That(IssueWritePolicy.FormatList(Array.Empty<string>())).IsEqualTo(string.Empty);
    }

    [Test]
    [Arguments("DEV-42", true)]
    [Arguments("dev-42", true)]
    [Arguments(" DEV-42 ", true)]
    [Arguments("DEV-421", false)]
    [Arguments("OPS-7", false)]
    [Arguments("", false)]
    [Arguments(null, false)]
    public async Task IsAllowed_ComparesCaseInsensitively_AndDeniesUnknown(string? key, bool expected)
    {
        await Assert.That(IssueWritePolicy.IsAllowed(new[] { "DEV-42" }, key)).IsEqualTo(expected);
    }

    [Test]
    public async Task IsAllowed_NoRestriction_AllowsEverything()
    {
        await Assert.That(IssueWritePolicy.IsAllowed(null, "OPS-7")).IsTrue();
        await Assert.That(IssueWritePolicy.IsAllowed(Array.Empty<string>(), null)).IsTrue();
    }

    [Test]
    public async Task DeniedMessage_NamesIssueProfileAndPolicy()
    {
        var message = IssueWritePolicy.DeniedMessage("OPS-7", "ci", new[] { "DEV-42" });

        await Assert.That(message)
            .IsEqualTo("issue 'OPS-7' is outside allowed_write_issues of profile 'ci' (allowed: DEV-42)");
    }

    [Test]
    public async Task Denied_UsesPolicyViolationCode()
    {
        var ex = IssueWritePolicy.Denied("OPS-7", "ci", new[] { "DEV-42" });

        await Assert.That(ex.Code).IsEqualTo(ErrorCode.PolicyViolation);
    }

    [Test]
    public async Task DeniedUnscopedMessage_NamesOperationAndPolicy()
    {
        var message = IssueWritePolicy.DeniedUnscopedMessage("POST issues", "ci", new[] { "DEV-42" });

        await Assert.That(message).Contains("POST issues");
        await Assert.That(message).Contains("allowed_write_issues of profile 'ci'");
        await Assert.That(message).Contains("DEV-42");
    }

    [Test]
    [Arguments("""{"text":"hi","summonees":["u"]}""", "summonees")]
    [Arguments("""{"text":"hi","maillistSummonees":["m@e.com"]}""", "maillistSummonees")]
    [Arguments("""{"comment":{"text":"hi","summonees":["u"]}}""", "summonees")]
    [Arguments("""{"items":[{"summonees":[]}]}""", "summonees")]
    [Arguments("""{"Summonees":["u"]}""", "summonees")]
    public async Task FindNotificationField_FindsFieldAtAnyDepth(string body, string expected)
    {
        using var doc = JsonDocument.Parse(body);

        await Assert.That(IssueWritePolicy.FindNotificationField(doc.RootElement)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""{"text":"hi"}""")]
    [Arguments("""{"summary":"s","description":"summonees are mentioned only in text"}""")]
    [Arguments("""[{"text":"hi"}]""")]
    [Arguments("""null""")]
    public async Task FindNotificationField_ReturnsNull_WhenAbsent(string body)
    {
        using var doc = JsonDocument.Parse(body);

        await Assert.That(IssueWritePolicy.FindNotificationField(doc.RootElement)).IsNull();
    }
}
