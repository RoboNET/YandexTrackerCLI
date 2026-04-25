namespace YandexTrackerCLI.Tests.Interactive;

using TUnit.Core;
using YandexTrackerCLI.Interactive;

/// <summary>
/// Тесты для <see cref="ConsoleTokenReader"/>. Мутируют <see cref="Console.In"/>
/// и потому выполняются последовательно с другими тестами, использующими
/// глобальное состояние консоли.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class ConsoleTokenReaderTests
{
    /// <summary>
    /// <c>ReadLine()</c> должен возвращать то, что подложено в
    /// <see cref="Console.In"/>. Это косвенно верифицирует, что реализация
    /// использует <see cref="Console.ReadLine"/>.
    /// </summary>
    [Test]
    public async Task ReadLine_ReturnsConsoleInput()
    {
        var prev = Console.In;
#pragma warning disable TUnit0055
        Console.SetIn(new StringReader("y0_TEST_TOKEN\n"));
        try
        {
            var reader = new ConsoleTokenReader();
            await Assert.That(reader.ReadLine()).IsEqualTo("y0_TEST_TOKEN");
        }
        finally
        {
            Console.SetIn(prev);
        }
#pragma warning restore TUnit0055
    }
}
