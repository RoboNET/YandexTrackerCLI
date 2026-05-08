namespace YandexTrackerCLI.Input;

using System.Text;
using System.Text.Json;
using Core.Api.Errors;

/// <summary>
/// Сливает raw JSON-payload (из <c>--json-file</c>/<c>--json-stdin</c>)
/// с typed inline-override'ами в один компактный JSON-объект.
/// AOT-friendly: использует <see cref="Utf8JsonWriter"/> и без рефлексии.
/// </summary>
public static class JsonBodyMerger
{
    /// <summary>
    /// Override-значение для merge. Поддерживает string/bool/long/null.
    /// Discriminated-union через флаг типа — вместо <c>object</c>, чтобы
    /// не уйти в reflection-сериализацию.
    /// </summary>
    public readonly record struct OverrideValue
    {
        private enum Kind : byte { String, Bool, Long, Null }

        private readonly Kind _kind;
        private readonly string? _str;
        private readonly bool _bool;
        private readonly long _long;

        private OverrideValue(Kind k, string? s, bool b, long l)
        {
            _kind = k; _str = s; _bool = b; _long = l;
        }

        /// <summary>Создаёт строковое override-значение.</summary>
        public static OverrideValue Of(string s) => new(Kind.String, s, default, default);

        /// <summary>Создаёт булевское override-значение.</summary>
        public static OverrideValue Of(bool b)   => new(Kind.Bool,   null, b, default);

        /// <summary>Создаёт числовое (long) override-значение.</summary>
        public static OverrideValue Of(long n)   => new(Kind.Long,   null, default, n);

        /// <summary>Override-значение JSON <c>null</c>.</summary>
        public static OverrideValue Null         => new(Kind.Null,   null, default, default);

        internal void Write(Utf8JsonWriter w, string name)
        {
            switch (_kind)
            {
                case Kind.String: w.WriteString(name, _str); break;
                case Kind.Bool:   w.WriteBoolean(name, _bool); break;
                case Kind.Long:   w.WriteNumber(name, _long); break;
                case Kind.Null:   w.WriteNull(name); break;
            }
        }
    }

    /// <summary>
    /// Сливает <paramref name="rawJson"/> с <paramref name="overrides"/>.
    /// Возвращает компактный JSON-объект либо <c>null</c>, если оба входа пусты.
    /// </summary>
    /// <param name="rawJson">Сырой JSON-объект либо <c>null</c>.</param>
    /// <param name="overrides">Список typed inline-override'ов; последняя запись по ключу побеждает.</param>
    /// <returns>Компактный JSON-объект либо <c>null</c>, если оба входа пусты.</returns>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.InvalidArgs"/>: rawJson не object (массив/литерал).
    /// </exception>
    public static string? Merge(
        string? rawJson,
        IReadOnlyList<(string Key, OverrideValue Value)> overrides)
    {
        var hasOverrides = overrides.Count > 0;
        if (string.IsNullOrWhiteSpace(rawJson) && !hasOverrides)
        {
            return null;
        }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();

            var written = new HashSet<string>(StringComparer.Ordinal);
            var ovIndex = BuildOverrideIndex(overrides);

            if (!string.IsNullOrWhiteSpace(rawJson))
            {
                using var doc = JsonDocument.Parse(rawJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "JSON body must be an object to merge inline overrides.");
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (ovIndex.TryGetValue(prop.Name, out var ov))
                    {
                        ov.Write(w, prop.Name);
                    }
                    else
                    {
                        w.WritePropertyName(prop.Name);
                        prop.Value.WriteTo(w);
                    }
                    written.Add(prop.Name);
                }
            }

            foreach (var (key, val) in overrides)
            {
                if (written.Add(key))
                {
                    ovIndex[key].Write(w, key);
                }
            }

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static Dictionary<string, OverrideValue> BuildOverrideIndex(
        IReadOnlyList<(string Key, OverrideValue Value)> overrides)
    {
        var d = new Dictionary<string, OverrideValue>(overrides.Count, StringComparer.Ordinal);
        foreach (var (k, v) in overrides)
        {
            d[k] = v; // last write wins on duplicate
        }
        return d;
    }
}
