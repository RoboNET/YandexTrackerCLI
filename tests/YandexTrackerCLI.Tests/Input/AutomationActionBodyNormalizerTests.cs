namespace YandexTrackerCLI.Tests.Input;

using System.Text.Json;
using TUnit.Core;
using YandexTrackerCLI.Input;

/// <summary>
/// Тесты <see cref="AutomationActionBodyNormalizer"/>: конвертация действий
/// <c>Update</c> из read-формата (как отдаёт GET) в write-формат (как принимает
/// POST/PATCH) и проброс всего остального без изменений.
/// </summary>
public sealed class AutomationActionBodyNormalizerTests
{
    /// <summary>
    /// Read-формат с одним полем превращается в плоский словарь
    /// <c>&lt;fieldId&gt;: &lt;value&gt;</c>, обёртка <c>{"set": X}</c> разворачивается
    /// в голое значение — именно это подтверждено как рабочий write-формат.
    /// </summary>
    [Test]
    public async Task Normalize_ReadFormatSingleField_ConvertsToFlatDictionary()
    {
        var raw = """
        {"actions":[{"type":"Update","id":3,"update":[
          {"field":{"self":"https://api/localFields/frontier","id":"6a84--frontier","display":"Рубеж"},
           "update":{"set":90}}]}]}
        """;

        var result = AutomationActionBodyNormalizer.Normalize(raw);

        using var doc = JsonDocument.Parse(result);
        var action = doc.RootElement.GetProperty("actions")[0];
        await Assert.That(action.GetProperty("type").GetString()).IsEqualTo("Update");
        await Assert.That(action.GetProperty("id").GetInt32()).IsEqualTo(3);

        var update = action.GetProperty("update");
        await Assert.That(update.ValueKind).IsEqualTo(JsonValueKind.Object);
        var value = update.GetProperty("6a84--frontier");
        await Assert.That(value.ValueKind).IsEqualTo(JsonValueKind.Number);
        await Assert.That(value.GetInt32()).IsEqualTo(90);
    }

    /// <summary>
    /// Несколько полей в read-массиве попадают в один write-объект.
    /// </summary>
    [Test]
    public async Task Normalize_ReadFormatMultipleFields_ConvertsAll()
    {
        var raw = """
        {"actions":[{"type":"Update","update":[
          {"field":{"id":"summary"},"update":"New summary"},
          {"field":{"id":"tags"},"update":{"add":["a","b"]}}]}]}
        """;

        var result = AutomationActionBodyNormalizer.Normalize(raw);

        using var doc = JsonDocument.Parse(result);
        var update = doc.RootElement.GetProperty("actions")[0].GetProperty("update");
        await Assert.That(update.GetProperty("summary").GetString()).IsEqualTo("New summary");
        // add — multi-value оператор, разворачивать его нельзя.
        await Assert.That(update.GetProperty("tags").GetProperty("add").GetArrayLength()).IsEqualTo(2);
    }

    /// <summary>
    /// Тело, уже находящееся в write-формате (<c>update</c> — объект),
    /// возвращается посимвольно без изменений.
    /// </summary>
    [Test]
    public async Task Normalize_AlreadyWriteFormat_ReturnsInputUnchanged()
    {
        var raw = """{"actions":[{"type":"Update","update":{"summary":"x","tags":{"add":"a"}}}]}""";

        var result = AutomationActionBodyNormalizer.Normalize(raw);

        await Assert.That(result).IsEqualTo(raw);
    }

    /// <summary>
    /// Действия других типов (например <c>Transition</c>) не трогаются.
    /// </summary>
    [Test]
    public async Task Normalize_NonUpdateAction_ReturnsInputUnchanged()
    {
        var raw = """{"actions":[{"type":"Transition","status":{"id":"3","key":"closed"}}]}""";

        var result = AutomationActionBodyNormalizer.Normalize(raw);

        await Assert.That(result).IsEqualTo(raw);
    }

    /// <summary>
    /// Соседние действия других типов сохраняются как есть, когда в теле
    /// присутствует конвертируемое действие <c>Update</c>.
    /// </summary>
    [Test]
    public async Task Normalize_MixedActions_ConvertsOnlyUpdate()
    {
        var raw = """
        {"name":"a","actions":[
          {"type":"Transition","status":{"key":"closed"}},
          {"type":"Update","update":[{"field":{"id":"frontier"},"update":{"set":90}}]}]}
        """;

        var result = AutomationActionBodyNormalizer.Normalize(raw);

        using var doc = JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("a");
        var actions = doc.RootElement.GetProperty("actions");
        await Assert.That(actions.GetArrayLength()).IsEqualTo(2);
        await Assert.That(actions[0].GetProperty("status").GetProperty("key").GetString())
            .IsEqualTo("closed");
        await Assert.That(actions[1].GetProperty("update").GetProperty("frontier")
            .GetInt32()).IsEqualTo(90);
    }

    /// <summary>
    /// Прочие поля тела (<c>filter</c>, <c>conditions</c>, вложенные структуры)
    /// переносятся без изменений.
    /// </summary>
    [Test]
    public async Task Normalize_PreservesOtherBodyProperties()
    {
        var raw = """
        {"name":"a","filter":{"status":["open"]},"intervalMillis":1000,
         "actions":[{"type":"Update","update":[{"field":{"id":"f"},"update":{"set":1}}]}]}
        """;

        var result = AutomationActionBodyNormalizer.Normalize(raw);

        using var doc = JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("filter")
            .GetProperty("status").GetArrayLength()).IsEqualTo(1);
        await Assert.That(doc.RootElement.GetProperty("intervalMillis").GetInt32()).IsEqualTo(1000);
    }

    /// <summary>
    /// Тело без <c>actions</c> возвращается как есть.
    /// </summary>
    [Test]
    public async Task Normalize_BodyWithoutActions_ReturnsInputUnchanged()
    {
        var raw = """{"name":"a","active":true,"intervalMillis":600000}""";

        var result = AutomationActionBodyNormalizer.Normalize(raw);

        await Assert.That(result).IsEqualTo(raw);
    }

    /// <summary>
    /// Корень не объект (массив/литерал) — вход возвращается без изменений.
    /// </summary>
    [Test]
    public async Task Normalize_NonObjectRoot_ReturnsInputUnchanged()
    {
        var raw = """[{"actions":[{"type":"Update","update":[]}]}]""";

        await Assert.That(AutomationActionBodyNormalizer.Normalize(raw)).IsEqualTo(raw);
    }

    /// <summary>
    /// Битый read-элемент без <c>field.id</c> не роняет нормализацию:
    /// действие пробрасывается без изменений (решение остаётся за API).
    /// </summary>
    [Test]
    public async Task Normalize_ReadEntryWithoutFieldId_ReturnsInputUnchanged()
    {
        var raw = """{"actions":[{"type":"Update","update":[{"field":{"display":"Рубеж"},"update":{"set":90}}]}]}""";

        await Assert.That(AutomationActionBodyNormalizer.Normalize(raw)).IsEqualTo(raw);
    }

    /// <summary>
    /// Повторяющийся идентификатор поля дал бы дубликат ключа в объекте —
    /// такое действие не конвертируется, тело возвращается как есть.
    /// </summary>
    [Test]
    public async Task Normalize_DuplicateFieldIds_ReturnsInputUnchanged()
    {
        var raw = """
        {"actions":[{"type":"Update","update":[
          {"field":{"id":"f"},"update":{"set":1}},
          {"field":{"id":"f"},"update":{"set":2}}]}]}
        """;

        await Assert.That(AutomationActionBodyNormalizer.Normalize(raw)).IsEqualTo(raw);
    }

    /// <summary>
    /// Пустой read-массив <c>update</c> конвертировать не во что — вход как есть.
    /// </summary>
    [Test]
    public async Task Normalize_EmptyUpdateArray_ReturnsInputUnchanged()
    {
        var raw = """{"actions":[{"type":"Update","update":[]}]}""";

        await Assert.That(AutomationActionBodyNormalizer.Normalize(raw)).IsEqualTo(raw);
    }

    /// <summary>
    /// Невалидный JSON не бросает исключение — валидацию делает
    /// <see cref="YandexTrackerCLI.Input.JsonBodyReader"/> раньше по пути.
    /// </summary>
    [Test]
    public async Task Normalize_InvalidJson_ReturnsInputUnchanged()
    {
        var raw = """{"actions":[{"type":"Update",""";

        await Assert.That(AutomationActionBodyNormalizer.Normalize(raw)).IsEqualTo(raw);
    }

    /// <summary>
    /// Пустая строка обрабатывается без исключения.
    /// </summary>
    [Test]
    public async Task Normalize_EmptyString_ReturnsInputUnchanged()
    {
        await Assert.That(AutomationActionBodyNormalizer.Normalize(string.Empty))
            .IsEqualTo(string.Empty);
    }

    /// <summary>
    /// <c>{"set": X}</c> разворачивается в голое <c>X</c>: для скалярного присваивания
    /// подтверждён только литеральный write-формат.
    /// </summary>
    [Test]
    public async Task Normalize_SetWrapper_IsUnwrappedToBareValue()
    {
        var raw = """{"actions":[{"type":"Update","update":[{"field":{"id":"X"},"update":{"set":90}}]}]}""";

        var result = AutomationActionBodyNormalizer.Normalize(raw);

        using var doc = JsonDocument.Parse(result);
        var value = doc.RootElement.GetProperty("actions")[0].GetProperty("update").GetProperty("X");
        await Assert.That(value.ValueKind).IsEqualTo(JsonValueKind.Number);
        await Assert.That(value.GetInt32()).IsEqualTo(90);
    }

    /// <summary>
    /// Обёртка <c>{"add": …}</c> — multi-value семантика, остаётся объектом как есть.
    /// </summary>
    [Test]
    public async Task Normalize_AddWrapper_StaysWrapped()
    {
        var raw = """{"actions":[{"type":"Update","update":[{"field":{"id":"X"},"update":{"add":"tag"}}]}]}""";

        var result = AutomationActionBodyNormalizer.Normalize(raw);

        using var doc = JsonDocument.Parse(result);
        var value = doc.RootElement.GetProperty("actions")[0].GetProperty("update").GetProperty("X");
        await Assert.That(value.ValueKind).IsEqualTo(JsonValueKind.Object);
        await Assert.That(value.GetProperty("add").GetString()).IsEqualTo("tag");
    }

    /// <summary>
    /// Составной объект (<c>set</c> — не единственный ключ) неоднозначен,
    /// поэтому переносится целиком без разворачивания.
    /// </summary>
    [Test]
    public async Task Normalize_CompositeOperatorObject_StaysAsObject()
    {
        var raw = """{"actions":[{"type":"Update","update":[{"field":{"id":"X"},"update":{"set":90,"foo":1}}]}]}""";

        var result = AutomationActionBodyNormalizer.Normalize(raw);

        using var doc = JsonDocument.Parse(result);
        var value = doc.RootElement.GetProperty("actions")[0].GetProperty("update").GetProperty("X");
        await Assert.That(value.ValueKind).IsEqualTo(JsonValueKind.Object);
        await Assert.That(value.GetProperty("set").GetInt32()).IsEqualTo(90);
        await Assert.That(value.GetProperty("foo").GetInt32()).IsEqualTo(1);
    }

    /// <summary>
    /// Значение-литерал в read-формате переносится без изменений.
    /// </summary>
    [Test]
    public async Task Normalize_LiteralValue_IsCopiedAsIs()
    {
        var raw = """{"actions":[{"type":"Update","update":[{"field":{"id":"description"},"update":"New issue"}]}]}""";

        var result = AutomationActionBodyNormalizer.Normalize(raw);

        using var doc = JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("actions")[0].GetProperty("update")
            .GetProperty("description").GetString()).IsEqualTo("New issue");
    }

    /// <summary>
    /// Запись read-массива с неизвестным ключом помимо <c>field</c>/<c>update</c>
    /// не конвертируется: перенос потерял бы данные.
    /// </summary>
    [Test]
    public async Task Normalize_ReadEntryWithUnknownProperty_ReturnsInputUnchanged()
    {
        var raw = """{"actions":[{"type":"Update","update":[{"field":{"id":"X"},"update":{"set":1},"scope":"all"}]}]}""";

        await Assert.That(AutomationActionBodyNormalizer.Normalize(raw)).IsEqualTo(raw);
    }
}
