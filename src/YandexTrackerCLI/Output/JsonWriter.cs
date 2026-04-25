namespace YandexTrackerCLI.Output;

using System.Text.Json;

/// <summary>
/// Диспетчер форматов вывода: принимает резолвленный (не <see cref="OutputFormat.Auto"/>)
/// формат и делегирует рендер соответствующему рендереру (Json/Minimal/Table).
/// </summary>
public static class JsonWriter
{
    /// <summary>
    /// Пишет <paramref name="element"/> в <paramref name="writer"/> в указанном формате.
    /// </summary>
    /// <param name="writer">Куда писать (обычно <see cref="Console.Out"/>).</param>
    /// <param name="element">Сериализуемый JSON-элемент.</param>
    /// <param name="format">Целевой формат; <see cref="OutputFormat.Auto"/> запрещён —
    /// должен быть резолвлен через <see cref="FormatResolver"/> до вызова.</param>
    /// <param name="pretty">Используется только для <see cref="OutputFormat.Json"/>:
    /// включает отступы и trailing newline.</param>
    /// <exception cref="InvalidOperationException">Если <paramref name="format"/> равен
    /// <see cref="OutputFormat.Auto"/>.</exception>
    public static void Write(TextWriter writer, JsonElement element, OutputFormat format, bool pretty)
    {
        switch (format)
        {
            case OutputFormat.Auto:
                throw new InvalidOperationException(
                    "OutputFormat.Auto must be resolved via FormatResolver before reaching JsonWriter.");

            case OutputFormat.Json:
                WriteJson(writer, element, pretty);
                break;

            case OutputFormat.Minimal:
                MinimalRenderer.Render(writer, element);
                break;

            case OutputFormat.Table:
                TableRenderer.Render(writer, element);
                break;

            default:
                throw new InvalidOperationException($"Unknown OutputFormat: {format}");
        }
    }

    private static void WriteJson(TextWriter writer, JsonElement element, bool pretty)
    {
        using var ms = new MemoryStream();
        using (var jw = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = pretty }))
        {
            element.WriteTo(jw);
        }
        writer.Write(System.Text.Encoding.UTF8.GetString(ms.ToArray()));
        if (pretty)
        {
            writer.WriteLine();
        }
    }
}
