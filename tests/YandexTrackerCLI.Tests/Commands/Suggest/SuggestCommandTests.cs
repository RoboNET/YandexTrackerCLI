namespace YandexTrackerCLI.Tests.Commands.Suggest;

using TUnit.Core;
using YandexTrackerCLI.Commands.Suggest;

/// <summary>
/// Unit-тесты для <see cref="SuggestCommand.BuildSuggestPath"/>: чистая URL-сборка
/// без HTTP и Console-state, поэтому параллелизм допустим.
/// </summary>
public sealed class SuggestCommandTests
{
    /// <summary>
    /// Простой ASCII-ввод с пробелом, без queue — пробел кодируется как <c>%20</c>.
    /// </summary>
    [Test]
    public async Task BuildSuggestPath_AsciiInput_NoQueue()
    {
        var path = SuggestCommand.BuildSuggestPath("fix bug", null);
        await Assert.That(path).IsEqualTo("issues/_suggest?input=fix%20bug");
    }

    /// <summary>
    /// Кириллический ввод и явный queue — оба значения URL-encoded, queue
    /// добавляется через <c>&amp;queue=</c>.
    /// </summary>
    [Test]
    public async Task BuildSuggestPath_CyrillicInput_WithQueue()
    {
        var path = SuggestCommand.BuildSuggestPath("тест", "DEV");
        await Assert.That(path).IsEqualTo("issues/_suggest?input=%D1%82%D0%B5%D1%81%D1%82&queue=DEV");
    }

    /// <summary>
    /// Пустая строка тоже даёт валидный путь — параметр <c>input=</c> сохраняется,
    /// чтобы API получил явно пустой запрос.
    /// </summary>
    [Test]
    public async Task BuildSuggestPath_EmptyInput()
    {
        var path = SuggestCommand.BuildSuggestPath(string.Empty, null);
        await Assert.That(path).IsEqualTo("issues/_suggest?input=");
    }
}
