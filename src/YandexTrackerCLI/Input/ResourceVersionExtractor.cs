namespace YandexTrackerCLI.Input;

using System.Text;
using System.Text.Json;

/// <summary>
/// Извлекает и вырезает корневое поле <c>version</c> из тела PATCH-запроса.
/// <para>
/// GET отдаёт ресурс (задачу, компонент, триггер, автодействие) вместе с полем
/// <c>version</c>, но API на запись его не принимает: <c>PATCH</c> с <c>version</c>
/// в теле отвечает
/// <c>400 version: Incorrect data format</c>, а версию ждёт query-параметром
/// (<c>?version=N</c>) либо заголовком <c>If-Match</c>. Чтобы round-trip
/// «<c>get</c> → правка → <c>update</c>» работал без ручной чистки JSON, CLI сам
/// убирает поле из тела и подставляет его значение в query.
/// </para>
/// AOT-friendly: только <see cref="JsonDocument"/> / <see cref="Utf8JsonWriter"/>, без рефлексии.
/// </summary>
public static class ResourceVersionExtractor
{
    private const string VersionProperty = "version";

    /// <summary>
    /// Убирает корневое числовое поле <c>version</c> из JSON-объекта и возвращает его значение.
    /// </summary>
    /// <param name="rawJson">Тело запроса.</param>
    /// <param name="version">
    /// Значение вырезанного поля либо <c>null</c>, если поля нет, оно не число или корень не объект.
    /// </param>
    /// <returns>
    /// Тело без поля <c>version</c> (порядок и содержимое остальных полей сохранены)
    /// либо исходная строка без изменений, если вырезать нечего.
    /// </returns>
    /// <remarks>
    /// Метод не бросает исключений на неожиданном JSON: валидация тела —
    /// забота <see cref="JsonBodyReader"/>.
    /// Нечисловое <c>version</c> (например строка) остаётся в теле как есть — угадывать
    /// намерение пользователя нельзя, пусть отвечает API.
    /// </remarks>
    public static string StripVersion(string rawJson, out long? version)
    {
        version = null;

        // Дешёвый ранний выход: без ключа "version" делать нечего.
        if (string.IsNullOrWhiteSpace(rawJson) ||
            !rawJson.Contains("\"" + VersionProperty + "\"", StringComparison.Ordinal))
        {
            return rawJson;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawJson);
        }
        catch (JsonException)
        {
            return rawJson;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty(VersionProperty, out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt64(out var parsed))
            {
                return rawJson;
            }

            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
            {
                w.WriteStartObject();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.NameEquals(VersionProperty))
                    {
                        continue;
                    }

                    w.WritePropertyName(prop.Name);
                    prop.Value.WriteTo(w);
                }
                w.WriteEndObject();
            }

            version = parsed;
            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }
}
