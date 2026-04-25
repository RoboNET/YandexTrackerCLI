namespace YandexTrackerCLI.Output;

using System.Text;
using System.Text.Json;

/// <summary>
/// Рендерер краткого формата вывода: одно identifying-поле на строку.
/// Подходит для пайплайнов и быстрого просмотра.
/// </summary>
/// <remarks>
/// AOT-safe: работает только с <see cref="JsonElement"/> и <see cref="JsonValueKind"/>,
/// не использует reflection-based serializer.
/// </remarks>
public static class MinimalRenderer
{
    /// <summary>
    /// Список identifying-полей в порядке предпочтения. Первое найденное в объекте
    /// используется как краткая идентификация.
    /// </summary>
    private static readonly string[] IdentifyingFields =
    {
        "key", "id", "login", "name", "display", "summary",
    };

    /// <summary>
    /// Рендерит элемент в minimal-формат.
    /// </summary>
    /// <param name="writer">Куда писать.</param>
    /// <param name="element">JSON элемент: object, array или primitive.</param>
    public static void Render(TextWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteLine(RenderObject(element));
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        writer.WriteLine(RenderObject(item));
                    }
                    else
                    {
                        writer.WriteLine(RenderPrimitive(item));
                    }
                }
                break;

            default:
                writer.WriteLine(RenderPrimitive(element));
                break;
        }
    }

    /// <summary>
    /// Возвращает строковое представление объекта: первое найденное identifying-поле
    /// или, если ни одного нет, compact JSON всего объекта.
    /// </summary>
    private static string RenderObject(JsonElement obj)
    {
        foreach (var name in IdentifyingFields)
        {
            if (obj.TryGetProperty(name, out var prop))
            {
                var rendered = RenderPrimitive(prop);
                if (!string.IsNullOrEmpty(rendered))
                {
                    return rendered;
                }
            }
        }

        // Fallback: compact JSON всего объекта.
        return ToCompactJson(obj);
    }

    /// <summary>
    /// Печатает примитив без обрамляющих кавычек для строк; для объектов и массивов
    /// сериализует compact JSON.
    /// </summary>
    private static string RenderPrimitive(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String       => el.GetString() ?? string.Empty,
        JsonValueKind.Number       => el.GetRawText(),
        JsonValueKind.True         => "true",
        JsonValueKind.False        => "false",
        JsonValueKind.Null         => string.Empty,
        JsonValueKind.Object       => ToCompactJson(el),
        JsonValueKind.Array        => ToCompactJson(el),
        _                          => el.GetRawText(),
    };

    /// <summary>
    /// Сериализует <paramref name="el"/> в compact JSON через <see cref="Utf8JsonWriter"/>
    /// (AOT-safe, без reflection).
    /// </summary>
    private static string ToCompactJson(JsonElement el)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            el.WriteTo(w);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
