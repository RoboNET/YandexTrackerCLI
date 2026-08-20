namespace YandexTrackerCLI.Output;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Core.Api.Errors;

public static class ErrorWriter
{
    /// <summary>
    /// Кодировщик, оставляющий не-ASCII символы как есть.
    /// </summary>
    /// <remarks>
    /// Дефолтный кодировщик экранирует всё за пределами ASCII, и сообщение на русском
    /// доезжает до stderr в виде <c>Ве...</c> — формально валидный JSON, который
    /// человек прочитать не может. <see cref="UnicodeRanges.All"/> снимает именно это
    /// экранирование, оставляя нетронутым экранирование HTML-опасных символов
    /// (<c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>) — в отличие от
    /// <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>.
    /// </remarks>
    private static readonly JavaScriptEncoder MessageEncoder = JavaScriptEncoder.Create(UnicodeRanges.All);

    public static void Write(TextWriter stderr, TrackerException ex) =>
        stderr.WriteLine(Render(ex));

    /// <summary>
    /// Собирает JSON-представление ошибки без записи — для случаев, когда писать приходится
    /// не в <see cref="TextWriter"/> (например, напрямую в файловый дескриптор stderr).
    /// </summary>
    /// <param name="ex">Ошибка.</param>
    /// <returns>Строка JSON без завершающего перевода строки.</returns>
    public static string Render(TrackerException ex)
    {
        var err = ex.ToError();
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = MessageEncoder }))
        {
            w.WriteStartObject();
            w.WriteStartObject("error");
            w.WriteString("code", err.Code);
            w.WriteString("message", err.Message);
            if (err.HttpStatus is { } s)       w.WriteNumber("http_status", s);
            if (err.TraceId is { } t)          w.WriteString("trace_id", t);
            if (err.ReloginCommand is { } r)   w.WriteString("relogin_command", r);
            w.WriteEndObject();
            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
