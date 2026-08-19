namespace YandexTrackerCLI.Input;

using System.Text;
using System.Text.Json;

/// <summary>
/// Приводит тело запроса автоматизации (триггеры и автодействия) к write-формату,
/// который принимает API на <c>POST</c>/<c>PATCH</c>.
/// <para>
/// API отдаёт действие <c>type: "Update"</c> в read-формате — массивом записей
/// <c>{"field":{"id":"…"},"update":{"set":…}}</c>, а на запись принимает только
/// плоский словарь <c>{"&lt;fieldId&gt;": &lt;value&gt;}</c>.
/// </para>
/// <para>
/// Значение-обёртка <c>{"set": X}</c> при переносе разворачивается в голое <c>X</c>:
/// именно присваивание литералом подтверждено как рабочий write-формат для скалярных
/// полей, тогда как <c>set</c>/<c>add</c>/<c>remove</c> документированы для multi-value.
/// Обёртки <c>add</c>/<c>remove</c> и любые составные объекты (несколько ключей)
/// переносятся как есть — их семантику домысливать нельзя.
/// </para>
/// AOT-friendly: только <see cref="JsonDocument"/> / <see cref="Utf8JsonWriter"/>, без рефлексии.
/// </summary>
public static class AutomationActionBodyNormalizer
{
    private const string ActionsProperty = "actions";
    private const string TypeProperty = "type";
    private const string UpdateProperty = "update";
    private const string FieldProperty = "field";
    private const string IdProperty = "id";
    private const string UpdateActionType = "Update";
    private const string SetOperator = "set";

    /// <summary>
    /// Нормализует действия <c>Update</c> в теле запроса из read-формата в write-формат,
    /// разворачивая по пути значения-обёртки <c>{"set": X}</c> в голое <c>X</c>.
    /// </summary>
    /// <param name="rawJson">
    /// Тело запроса (обычно результат <see cref="JsonBodyReader.ReadAndMerge"/>).
    /// </param>
    /// <returns>
    /// Тело в write-формате. Возвращается исходная строка без изменений, если
    /// нормализовать нечего: корень не объект, нет массива <c>actions</c>, ни одно
    /// действие не в read-формате либо JSON не разбирается.
    /// </returns>
    /// <remarks>
    /// Метод намеренно не бросает исключений на невалидном или неожиданном JSON:
    /// валидацию тела делает <see cref="JsonBodyReader"/>, а всё, что нормализатор
    /// не понимает (действие без <c>field.id</c>, повторяющиеся идентификаторы полей,
    /// лишние ключи у записи read-массива, пустой массив <c>update</c>), пробрасывается
    /// как есть — решение остаётся за API.
    /// </remarks>
    public static string Normalize(string rawJson)
    {
        // Дешёвый ранний выход: без ключа "actions" нормализовать заведомо нечего.
        if (string.IsNullOrWhiteSpace(rawJson) ||
            !rawJson.Contains("\"" + ActionsProperty + "\"", StringComparison.Ordinal))
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
                !root.TryGetProperty(ActionsProperty, out var actions) ||
                actions.ValueKind != JsonValueKind.Array ||
                !HasReadFormatUpdateAction(actions))
            {
                return rawJson;
            }

            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
            {
                w.WriteStartObject();
                foreach (var prop in root.EnumerateObject())
                {
                    w.WritePropertyName(prop.Name);
                    if (prop.NameEquals(ActionsProperty) && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        WriteActions(w, prop.Value);
                    }
                    else
                    {
                        prop.Value.WriteTo(w);
                    }
                }
                w.WriteEndObject();
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    /// <summary>
    /// Проверяет, есть ли в массиве действий хотя бы одно, которое нужно переписать.
    /// </summary>
    private static bool HasReadFormatUpdateAction(JsonElement actions)
    {
        foreach (var action in actions.EnumerateArray())
        {
            if (IsConvertibleUpdateAction(action))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Действие подлежит конвертации, только если это <c>Update</c> с массивом
    /// <c>update</c>, каждый элемент которого состоит ровно из <c>field.id</c> и
    /// вложенного <c>update</c>, а идентификаторы полей не повторяются. Иначе действие
    /// пробрасывается без изменений — конвертация не должна терять данные (в том числе
    /// незнакомые ключи, если Tracker расширит формат).
    /// </summary>
    private static bool IsConvertibleUpdateAction(JsonElement action)
    {
        if (action.ValueKind != JsonValueKind.Object ||
            !action.TryGetProperty(TypeProperty, out var type) ||
            type.ValueKind != JsonValueKind.String ||
            !string.Equals(type.GetString(), UpdateActionType, StringComparison.OrdinalIgnoreCase) ||
            !action.TryGetProperty(UpdateProperty, out var update) ||
            update.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in update.EnumerateArray())
        {
            if (!TryGetFieldId(entry, out var fieldId) ||
                !entry.TryGetProperty(UpdateProperty, out _) ||
                HasUnknownProperties(entry) ||
                !seen.Add(fieldId))
            {
                return false;
            }
        }

        return seen.Count > 0;
    }

    /// <summary>
    /// Извлекает идентификатор поля из read-элемента (<c>field.id</c>).
    /// </summary>
    private static bool TryGetFieldId(JsonElement entry, out string fieldId)
    {
        fieldId = string.Empty;
        if (entry.ValueKind != JsonValueKind.Object ||
            !entry.TryGetProperty(FieldProperty, out var field) ||
            field.ValueKind != JsonValueKind.Object ||
            !field.TryGetProperty(IdProperty, out var id) ||
            id.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = id.GetString();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        fieldId = value;
        return true;
    }

    /// <summary>
    /// Проверяет, есть ли у записи read-массива ключи помимо <c>field</c> и <c>update</c>.
    /// Такую запись конвертировать нельзя: перенос потерял бы неизвестные данные.
    /// </summary>
    private static bool HasUnknownProperties(JsonElement entry)
    {
        foreach (var prop in entry.EnumerateObject())
        {
            if (!prop.NameEquals(FieldProperty) && !prop.NameEquals(UpdateProperty))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Разворачивает значение-обёртку <c>{"set": X}</c> в <c>X</c>. Объекты с другим
    /// оператором (<c>add</c>/<c>remove</c>) и с несколькими ключами не разворачиваются.
    /// </summary>
    private static bool TryUnwrapSet(JsonElement value, out JsonElement inner)
    {
        inner = default;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var found = false;
        foreach (var prop in value.EnumerateObject())
        {
            if (found ||
                !string.Equals(prop.Name, SetOperator, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            inner = prop.Value;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// Пишет массив действий, переписывая только те, что распознаны как read-формат.
    /// </summary>
    private static void WriteActions(Utf8JsonWriter w, JsonElement actions)
    {
        w.WriteStartArray();
        foreach (var action in actions.EnumerateArray())
        {
            if (IsConvertibleUpdateAction(action))
            {
                WriteConvertedUpdateAction(w, action);
            }
            else
            {
                action.WriteTo(w);
            }
        }
        w.WriteEndArray();
    }

    /// <summary>
    /// Пишет действие <c>Update</c>, заменяя read-массив <c>update</c> плоским
    /// объектом <c>&lt;fieldId&gt;: &lt;value&gt;</c> (с разворачиванием <c>{"set": X}</c>);
    /// остальные поля действия — как есть.
    /// </summary>
    private static void WriteConvertedUpdateAction(Utf8JsonWriter w, JsonElement action)
    {
        w.WriteStartObject();
        foreach (var prop in action.EnumerateObject())
        {
            w.WritePropertyName(prop.Name);
            if (!prop.NameEquals(UpdateProperty) || prop.Value.ValueKind != JsonValueKind.Array)
            {
                prop.Value.WriteTo(w);
                continue;
            }

            w.WriteStartObject();
            foreach (var entry in prop.Value.EnumerateArray())
            {
                // Конвертируемость всех элементов уже проверена IsConvertibleUpdateAction.
                if (TryGetFieldId(entry, out var fieldId) &&
                    entry.TryGetProperty(UpdateProperty, out var value))
                {
                    w.WritePropertyName(fieldId);
                    if (TryUnwrapSet(value, out var scalar))
                    {
                        scalar.WriteTo(w);
                    }
                    else
                    {
                        value.WriteTo(w);
                    }
                }
            }
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }
}
