namespace YandexTrackerCLI.Core.Tests.Config;

using TUnit.Core;
using YandexTrackerCLI.Core.Api.Errors;
using YandexTrackerCLI.Core.Config;

/// <summary>
/// Тесты <see cref="EnvBool"/>: строгий разбор для ограничивающих переменных и мягкий —
/// для переменных, которые лишь включают необязательную возможность.
/// </summary>
public sealed class EnvBoolTests
{
    [Test]
    [Arguments("1")]
    [Arguments("true")]
    [Arguments("TRUE")]
    [Arguments("TrUe")]
    [Arguments("yes")]
    [Arguments("YES")]
    [Arguments("on")]
    [Arguments("ON")]
    [Arguments(" 1")]
    [Arguments("  true  ")]
    public async Task Classify_TruthySpellings(string raw) =>
        await Assert.That(EnvBool.Classify(raw)).IsEqualTo(EnvBoolValue.True);

    [Test]
    [Arguments("0")]
    [Arguments("false")]
    [Arguments("False")]
    [Arguments("no")]
    [Arguments("NO")]
    [Arguments("off")]
    [Arguments("OFF")]
    [Arguments(" 0 ")]
    public async Task Classify_FalsySpellings(string raw) =>
        await Assert.That(EnvBool.Classify(raw)).IsEqualTo(EnvBoolValue.False);

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("\t")]
    public async Task Classify_EmptyOrWhitespace_IsUnset(string? raw) =>
        await Assert.That(EnvBool.Classify(raw)).IsEqualTo(EnvBoolValue.Unset);

    [Test]
    [Arguments("enabled")]
    [Arguments("да")]
    [Arguments("2")]
    [Arguments("truthy")]
    public async Task Classify_Garbage_IsUnrecognized(string raw) =>
        await Assert.That(EnvBool.Classify(raw)).IsEqualTo(EnvBoolValue.Unrecognized);

    [Test]
    [Arguments("on", true)]
    [Arguments("YES", true)]
    [Arguments(" 1", true)]
    [Arguments("off", false)]
    [Arguments("NO", false)]
    [Arguments(null, false)]
    [Arguments("   ", false)]
    public async Task ResolveStrict_RecognisedValues(string? raw, bool expected)
    {
        var env = new Dictionary<string, string?> { ["YT_X"] = raw };
        await Assert.That(EnvBool.ResolveStrict(env, "YT_X", "the profile setting"))
            .IsEqualTo(expected);
    }

    [Test]
    public async Task ResolveStrict_Garbage_ThrowsConfigError_NamingVariableAndValue()
    {
        var env = new Dictionary<string, string?> { ["YT_X"] = " enabled " };
        var ex = Assert.Throws<TrackerException>(
            () => EnvBool.ResolveStrict(env, "YT_X", "the profile setting"));

        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.ConfigError);
        await Assert.That(ex.Code.ToExitCode()).IsEqualTo(9);
        await Assert.That(ex.Message).Contains("YT_X");
        await Assert.That(ex.Message).Contains("enabled");
        await Assert.That(ex.Message).Contains("the profile setting");
    }

    /// <summary>
    /// Мягкий разбор: включает всё, что непусто и не является явным отрицанием.
    /// Так ведёт себя <c>YT_HYPERLINKS</c> — переменная, которая лишь включает
    /// необязательную возможность и ничего не снимает. <c>YT_LOG_RAW</c> сюда НЕ
    /// относится: она снимает маскирование секретов и разбирается строго.
    /// </summary>
    [Test]
    [Arguments("1", true)]
    [Arguments("on", true)]
    [Arguments("whatever", true)]
    [Arguments("0", false)]
    [Arguments("FALSE", false)]
    [Arguments(" off ", false)]
    [Arguments(null, false)]
    [Arguments("", false)]
    public async Task IsTruthyLenient_KeepsHistoricSemantics(string? raw, bool expected) =>
        await Assert.That(EnvBool.IsTruthyLenient(raw)).IsEqualTo(expected);
}
