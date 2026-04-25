namespace YandexTrackerCLI.Tests.Output;

using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Output;

/// <summary>
/// Проверяет, что <see cref="CommandOutput.WriteSingleField"/> экранирует значения
/// через <see cref="System.Text.Json.Utf8JsonWriter"/> и формирует валидный JSON,
/// который может распарсить downstream JSON-парсер.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class CommandOutputTests
{
    /// <summary>
    /// Кавычки и обратные слэши в значении экранируются — JSON валиден, значение восстановимо.
    /// </summary>
    [Test]
    public async Task WriteSingleField_EscapesQuotesAndBackslashes()
    {
        var sw = new StringWriter();
        var prevOut = Console.Out;
#pragma warning disable TUnit0055 // Overwriting the Console writer can break TUnit logging — OK в изолированной утилите теста
        Console.SetOut(sw);
        try
        {
            CommandOutput.WriteSingleField("name", "he said \"hi\" \\ hi");
        }
        finally
        {
            Console.SetOut(prevOut);
        }
#pragma warning restore TUnit0055

        using var doc = JsonDocument.Parse(sw.ToString());
        await Assert.That(doc.RootElement.GetProperty("name").GetString())
            .IsEqualTo("he said \"hi\" \\ hi");
    }

    /// <summary>
    /// Управляющие символы (<c>\n</c>, <c>\t</c>) в значении не ломают парсер — JSON валиден,
    /// значение восстановимо один-в-один.
    /// </summary>
    [Test]
    public async Task WriteSingleField_AcceptsControlChars_ProducesValidJson()
    {
        var sw = new StringWriter();
        var prevOut = Console.Out;
#pragma warning disable TUnit0055 // Overwriting the Console writer can break TUnit logging — OK в изолированной утилите теста
        Console.SetOut(sw);
        try
        {
            CommandOutput.WriteSingleField("key", "line1\nline2\tend");
        }
        finally
        {
            Console.SetOut(prevOut);
        }
#pragma warning restore TUnit0055

        using var doc = JsonDocument.Parse(sw.ToString()); // не бросается → валидный JSON
        await Assert.That(doc.RootElement.GetProperty("key").GetString())
            .IsEqualTo("line1\nline2\tend");
    }
}
