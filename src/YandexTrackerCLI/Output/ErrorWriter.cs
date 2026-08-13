namespace YandexTrackerCLI.Output;

using System.Text.Json;
using Core.Api.Errors;

public static class ErrorWriter
{
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
        using (var w = new Utf8JsonWriter(ms))
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
