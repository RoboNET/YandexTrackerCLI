namespace YandexTrackerCLI.Tests.Input;

using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Input;

/// <summary>
/// Тесты <see cref="ResourceVersionExtractor"/>: вырезание корневого поля
/// <c>version</c> (которое отдаёт GET, но не принимает PATCH) и извлечение его значения.
/// </summary>
public sealed class ResourceVersionExtractorTests
{
    /// <summary>
    /// Числовое <c>version</c> извлекается, из тела удаляется, остальные поля сохраняются.
    /// </summary>
    [Test]
    public async Task StripVersion_NumericVersion_IsExtractedAndRemoved()
    {
        var raw = """{"id":17,"version":42,"name":"a","actions":[{"type":"Transition"}]}""";

        var result = ResourceVersionExtractor.StripVersion(raw, out var version);

        await Assert.That(version).IsEqualTo(42L);
        using var doc = JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.TryGetProperty("version", out _)).IsFalse();
        await Assert.That(doc.RootElement.GetProperty("id").GetInt32()).IsEqualTo(17);
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("a");
        await Assert.That(doc.RootElement.GetProperty("actions").GetArrayLength()).IsEqualTo(1);
    }

    /// <summary>
    /// Тело без <c>version</c> возвращается как есть, версия — <c>null</c>.
    /// </summary>
    [Test]
    public async Task StripVersion_NoVersion_ReturnsInputUnchanged()
    {
        var raw = """{"name":"a","active":true}""";

        var result = ResourceVersionExtractor.StripVersion(raw, out var version);

        await Assert.That(version).IsNull();
        await Assert.That(result).IsEqualTo(raw);
    }

    /// <summary>
    /// Нечисловое <c>version</c> не трогаем — угадывать намерение пользователя нельзя.
    /// </summary>
    [Test]
    public async Task StripVersion_NonNumericVersion_ReturnsInputUnchanged()
    {
        var raw = """{"name":"a","version":"42"}""";

        var result = ResourceVersionExtractor.StripVersion(raw, out var version);

        await Assert.That(version).IsNull();
        await Assert.That(result).IsEqualTo(raw);
    }

    /// <summary>
    /// Вложенное поле <c>version</c> (не в корне) остаётся на месте.
    /// </summary>
    [Test]
    public async Task StripVersion_NestedVersion_IsPreserved()
    {
        var raw = """{"name":"a","meta":{"version":9}}""";

        var result = ResourceVersionExtractor.StripVersion(raw, out var version);

        await Assert.That(version).IsNull();
        await Assert.That(result).IsEqualTo(raw);
    }

    /// <summary>
    /// Корень не объект — вход возвращается без изменений.
    /// </summary>
    [Test]
    public async Task StripVersion_NonObjectRoot_ReturnsInputUnchanged()
    {
        var raw = """[{"version":1}]""";

        var result = ResourceVersionExtractor.StripVersion(raw, out var version);

        await Assert.That(version).IsNull();
        await Assert.That(result).IsEqualTo(raw);
    }

    /// <summary>
    /// Невалидный JSON и пустая строка обрабатываются без исключения.
    /// </summary>
    [Test]
    public async Task StripVersion_InvalidJsonOrEmpty_ReturnsInputUnchanged()
    {
        var broken = """{"version":42,""";

        var result = ResourceVersionExtractor.StripVersion(broken, out var version);
        await Assert.That(version).IsNull();
        await Assert.That(result).IsEqualTo(broken);

        var empty = ResourceVersionExtractor.StripVersion(string.Empty, out var emptyVersion);
        await Assert.That(emptyVersion).IsNull();
        await Assert.That(empty).IsEqualTo(string.Empty);
    }
}
