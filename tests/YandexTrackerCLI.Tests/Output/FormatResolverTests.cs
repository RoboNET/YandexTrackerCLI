namespace YandexTrackerCLI.Tests.Output;

using TUnit.Core;
using Core.Api.Errors;
using YandexTrackerCLI.Output;

/// <summary>
/// Тесты cascade-резолвера формата вывода.
/// </summary>
public sealed class FormatResolverTests
{
    private static IReadOnlyDictionary<string, string?> EmptyEnv() =>
        new Dictionary<string, string?>();

    private static IReadOnlyDictionary<string, string?> EnvWith(string key, string? value) =>
        new Dictionary<string, string?> { [key] = value };

    [Test]
    public async Task CliFormat_NotAuto_OverridesEverything()
    {
        // CLI=Json должен побить env=Table.
        var env = EnvWith("YT_FORMAT", "table");
        var actual = FormatResolver.Resolve(
            cliFormat: OutputFormat.Json,
            env: env,
            profileDefaultFormat: "minimal",
            isOutputRedirected: false);
        await Assert.That(actual).IsEqualTo(OutputFormat.Json);
    }

    [Test]
    public async Task CliAuto_FallsBackToEnv()
    {
        var env = EnvWith("YT_FORMAT", "Minimal");
        var actual = FormatResolver.Resolve(
            cliFormat: OutputFormat.Auto,
            env: env,
            profileDefaultFormat: null,
            isOutputRedirected: true);
        await Assert.That(actual).IsEqualTo(OutputFormat.Minimal);
    }

    [Test]
    public async Task CliAuto_NoEnv_UsesProfileDefault()
    {
        var actual = FormatResolver.Resolve(
            cliFormat: OutputFormat.Auto,
            env: EmptyEnv(),
            profileDefaultFormat: "table",
            isOutputRedirected: true);
        await Assert.That(actual).IsEqualTo(OutputFormat.Table);
    }

    [Test]
    public async Task CliAuto_NoEnv_NoProfile_RedirectedTrue_ReturnsJson()
    {
        var actual = FormatResolver.Resolve(
            cliFormat: OutputFormat.Auto,
            env: EmptyEnv(),
            profileDefaultFormat: null,
            isOutputRedirected: true);
        await Assert.That(actual).IsEqualTo(OutputFormat.Json);
    }

    [Test]
    public async Task CliAuto_NoEnv_NoProfile_RedirectedFalse_ReturnsTable()
    {
        var actual = FormatResolver.Resolve(
            cliFormat: OutputFormat.Auto,
            env: EmptyEnv(),
            profileDefaultFormat: null,
            isOutputRedirected: false);
        await Assert.That(actual).IsEqualTo(OutputFormat.Table);
    }

    [Test]
    public async Task InvalidEnvFormat_Throws()
    {
        var env = EnvWith("YT_FORMAT", "xml");
        var ex = await Assert.That(() =>
                FormatResolver.Resolve(
                    cliFormat: OutputFormat.Auto,
                    env: env,
                    profileDefaultFormat: null,
                    isOutputRedirected: false))
            .Throws<TrackerException>();
        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.InvalidArgs);
    }

    [Test]
    public async Task InvalidProfileFormat_Throws()
    {
        var ex = await Assert.That(() =>
                FormatResolver.Resolve(
                    cliFormat: OutputFormat.Auto,
                    env: EmptyEnv(),
                    profileDefaultFormat: "yaml",
                    isOutputRedirected: false))
            .Throws<TrackerException>();
        await Assert.That(ex!.Code).IsEqualTo(ErrorCode.InvalidArgs);
    }

    [Test]
    [Arguments("json", OutputFormat.Json)]
    [Arguments("JSON", OutputFormat.Json)]
    [Arguments("Json", OutputFormat.Json)]
    [Arguments("minimal", OutputFormat.Minimal)]
    [Arguments("MINIMAL", OutputFormat.Minimal)]
    [Arguments("table", OutputFormat.Table)]
    [Arguments(" Table ", OutputFormat.Table)]
    [Arguments("auto", OutputFormat.Auto)]
    public async Task Parse_AcceptsCaseInsensitive(string input, OutputFormat expected)
    {
        var actual = FormatResolver.Parse(input, source: "test");
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task Parse_InvalidValue_Throws_WithSourceInMessage()
    {
        var ex = await Assert.That(() => FormatResolver.Parse("xml", source: "YT_FORMAT"))
            .Throws<TrackerException>();
        await Assert.That(ex!.Message).Contains("YT_FORMAT");
        await Assert.That(ex.Message).Contains("xml");
    }

    [Test]
    public async Task EmptyEnvValue_FallsThroughToProfile()
    {
        var env = EnvWith("YT_FORMAT", "");
        var actual = FormatResolver.Resolve(
            cliFormat: OutputFormat.Auto,
            env: env,
            profileDefaultFormat: "minimal",
            isOutputRedirected: false);
        await Assert.That(actual).IsEqualTo(OutputFormat.Minimal);
    }

    [Test]
    public async Task EnvAuto_FallsThroughToProfile()
    {
        // YT_FORMAT=auto explicit — равносильно отсутствию.
        var env = EnvWith("YT_FORMAT", "auto");
        var actual = FormatResolver.Resolve(
            cliFormat: OutputFormat.Auto,
            env: env,
            profileDefaultFormat: "table",
            isOutputRedirected: true);
        await Assert.That(actual).IsEqualTo(OutputFormat.Table);
    }
}
