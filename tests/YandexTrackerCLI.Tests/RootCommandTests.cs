namespace YandexTrackerCLI.Tests;

using System.CommandLine;
using System.IO;
using TUnit.Core;
using YandexTrackerCLI.Commands;

/// <summary>
/// Тесты корневой команды и её глобальных опций.
/// </summary>
public sealed class RootCommandTests
{
    /// <summary>
    /// <c>--help</c> должен упомянуть все глобальные опции корневой команды.
    /// </summary>
    [Test]
    public async Task Help_MentionsGlobalOptions()
    {
        var root = RootCommandBuilder.Build();
        var sw = new StringWriter();
        var config = new InvocationConfiguration { Output = sw, Error = sw };

        var exit = await root.Parse(new[] { "--help" }).InvokeAsync(config);

        await Assert.That(exit).IsEqualTo(0);
        var text = sw.ToString();
        await Assert.That(text).Contains("--profile");
        await Assert.That(text).Contains("--read-only");
        await Assert.That(text).Contains("--format");
        await Assert.That(text).Contains("--timeout");
        await Assert.That(text).Contains("--no-color");
    }

    /// <summary>
    /// <c>--version</c> возвращает 0 и выводит непустую строку.
    /// </summary>
    [Test]
    public async Task Version_ReturnsExitCode0_AndPrintsSomething()
    {
        var root = RootCommandBuilder.Build();
        var sw = new StringWriter();
        var config = new InvocationConfiguration { Output = sw, Error = sw };

        var exit = await root.Parse(new[] { "--version" }).InvokeAsync(config);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(sw.ToString().Trim().Length).IsGreaterThan(0);
    }

    /// <summary>
    /// Неизвестная команда возвращает ненулевой exit code и не падает.
    /// </summary>
    [Test]
    public async Task UnknownCommand_ReturnsNonZeroExit_WithoutCrash()
    {
        var root = RootCommandBuilder.Build();
        var sw = new StringWriter();
        var config = new InvocationConfiguration { Output = sw, Error = sw };

        var exit = await root.Parse(new[] { "does-not-exist" }).InvokeAsync(config);

        await Assert.That(exit).IsNotEqualTo(0);
    }
}
