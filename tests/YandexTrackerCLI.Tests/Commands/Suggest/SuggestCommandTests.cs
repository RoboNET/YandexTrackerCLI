namespace YandexTrackerCLI.Tests.Commands.Suggest;

using System.Text.Json;
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
        await Assert.That(path).IsEqualTo("issues/_suggest?input=fix%20bug&full=true");
    }

    /// <summary>
    /// Кириллический ввод и явный queue — оба значения URL-encoded, queue
    /// добавляется через <c>&amp;queue=</c>.
    /// </summary>
    [Test]
    public async Task BuildSuggestPath_CyrillicInput_WithQueue()
    {
        var path = SuggestCommand.BuildSuggestPath("тест", "DEV");
        await Assert.That(path).IsEqualTo("issues/_suggest?input=%D1%82%D0%B5%D1%81%D1%82&queue=DEV&full=true");
    }

    /// <summary>
    /// Пустая строка тоже даёт валидный путь — параметр <c>input=</c> сохраняется,
    /// чтобы API получил явно пустой запрос.
    /// </summary>
    [Test]
    public async Task BuildSuggestPath_EmptyInput()
    {
        var path = SuggestCommand.BuildSuggestPath(string.Empty, null);
        await Assert.That(path).IsEqualTo("issues/_suggest?input=&full=true");
    }

    /// <summary>
    /// Без <c>--queue</c> запрос уходит без очереди, поэтому сервер возвращает задачи любых
    /// очередей: ограничение профиля обязано действовать на результат, иначе пикер
    /// показывает чужие задачи с темами.
    /// </summary>
    [Test]
    public async Task FilterSuggestions_DropsIssuesOutsideAllowedQueues()
    {
        using var doc = JsonDocument.Parse(
            """[{"key":"DEV-1"},{"key":"OPS-7","summary":"secret"},{"key":"QA-2"}]""");

        var filtered = SuggestCommand.FilterSuggestions(doc.RootElement, 10, new[] { "DEV", "QA" });

        var keys = filtered.Select(e => e.GetProperty("key").GetString()!).ToArray();
        await Assert.That(keys).IsEquivalentTo(new[] { "DEV-1", "QA-2" });
    }

    /// <summary>
    /// Лимит считает показанные элементы: чужие задачи не занимают места в списке.
    /// </summary>
    [Test]
    public async Task FilterSuggestions_LimitCountsShownItemsOnly()
    {
        using var doc = JsonDocument.Parse(
            """[{"key":"OPS-1"},{"key":"DEV-1"},{"key":"OPS-2"},{"key":"QA-1"},{"key":"DEV-2"}]""");

        var filtered = SuggestCommand.FilterSuggestions(doc.RootElement, 2, new[] { "DEV", "QA" });

        var keys = filtered.Select(e => e.GetProperty("key").GetString()!).ToArray();
        await Assert.That(keys).IsEquivalentTo(new[] { "DEV-1", "QA-1" });
    }

    /// <summary>
    /// Элемент без разбираемого ключа при действующем ограничении отбрасывается (fail closed).
    /// </summary>
    [Test]
    public async Task FilterSuggestions_ItemWithoutKey_IsDropped()
    {
        using var doc = JsonDocument.Parse("""[{"summary":"no key"},{"key":"DEV-1"}]""");

        var filtered = SuggestCommand.FilterSuggestions(doc.RootElement, 10, new[] { "DEV" });

        var keys = filtered.Select(e => e.GetProperty("key").GetString()!).ToArray();
        await Assert.That(keys).IsEquivalentTo(new[] { "DEV-1" });
    }

    /// <summary>
    /// Регрессия: без ограничения выдача не фильтруется, лимит работает как раньше.
    /// </summary>
    [Test]
    public async Task FilterSuggestions_Unrestricted_KeepsEverythingUpToLimit()
    {
        using var doc = JsonDocument.Parse("""[{"key":"OPS-1"},{"key":"DEV-1"},{"key":"QA-1"}]""");

        var filtered = SuggestCommand.FilterSuggestions(doc.RootElement, 2, null);

        var keys = filtered.Select(e => e.GetProperty("key").GetString()!).ToArray();
        await Assert.That(keys).IsEquivalentTo(new[] { "OPS-1", "DEV-1" });
    }
}
