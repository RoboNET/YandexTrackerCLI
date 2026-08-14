namespace YandexTrackerCLI.Output;

using System.Text.Json;

/// <summary>
/// Renders the stderr warning envelope shared by every warning the CLI emits: a single
/// object with a top-level <c>warning</c> key holding <c>code</c>, <c>message</c> and any
/// warning-specific fields.
/// </summary>
/// <remarks>
/// Deliberately mirrors <see cref="ErrorWriter"/>: warnings and errors travel the same
/// channel, so a consumer distinguishes them by the root key alone and needs no second
/// parser. The envelope is observable output — jq filters and agents already match on it —
/// so its shape (root key, field names, one line, no indentation) is not free to change.
/// </remarks>
public static class WarningWriter
{
    /// <summary>
    /// Writes a warning envelope as a single line.
    /// </summary>
    /// <param name="stderr">Writer to emit the line on (normally <see cref="Console.Error"/>).</param>
    /// <param name="code">Stable machine-readable warning code, e.g. <c>refresh_token_not_saved</c>.</param>
    /// <param name="message">Human-readable explanation.</param>
    /// <param name="fields">Extra string fields appended after <c>message</c>, in order.</param>
    public static void Write(
        TextWriter stderr,
        string code,
        string message,
        params (string Name, string Value)[] fields) =>
        stderr.WriteLine(Render(code, message, fields));

    /// <summary>
    /// Builds the warning envelope without writing it.
    /// </summary>
    /// <param name="code">Stable machine-readable warning code.</param>
    /// <param name="message">Human-readable explanation.</param>
    /// <param name="fields">Extra string fields appended after <c>message</c>, in order.</param>
    /// <returns>JSON string without a trailing newline.</returns>
    public static string Render(
        string code,
        string message,
        params (string Name, string Value)[] fields)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteStartObject("warning");
            w.WriteString("code", code);
            w.WriteString("message", message);
            foreach (var (name, value) in fields)
            {
                w.WriteString(name, value);
            }

            w.WriteEndObject();
            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
