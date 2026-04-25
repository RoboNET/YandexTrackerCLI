namespace YandexTrackerCLI.Output;

using System.Text.Json;
using Core.Api.Errors;

public static class ErrorWriter
{
    public static void Write(TextWriter stderr, TrackerException ex)
    {
        var err = ex.ToError();
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteStartObject("error");
            w.WriteString("code", err.Code);
            w.WriteString("message", err.Message);
            if (err.HttpStatus is { } s) w.WriteNumber("http_status", s);
            if (err.TraceId is { } t)    w.WriteString("trace_id", t);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        stderr.WriteLine(System.Text.Encoding.UTF8.GetString(ms.ToArray()));
    }
}
