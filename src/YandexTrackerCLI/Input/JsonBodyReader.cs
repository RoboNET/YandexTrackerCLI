namespace YandexTrackerCLI.Input;

using System.Text.Json;
using Core.Api.Errors;

/// <summary>
/// Помощник чтения JSON-payload для CLI-флагов <c>--json-file</c> / <c>--json-stdin</c>.
/// </summary>
public static class JsonBodyReader
{
    /// <summary>
    /// Читает JSON-payload из файла или stdin. Возвращает сырую строку
    /// (после валидации через <see cref="JsonDocument.Parse(string, JsonDocumentOptions)"/>).
    /// Возвращает <c>null</c>, если ни один источник не указан.
    /// </summary>
    /// <param name="filePath">Путь к файлу с JSON (флаг <c>--json-file</c>). Может быть <c>null</c>.</param>
    /// <param name="fromStdin">Если <c>true</c>, читать из <paramref name="stdinReader"/> (флаг <c>--json-stdin</c>).</param>
    /// <param name="stdinReader">Источник stdin. Обязателен при <paramref name="fromStdin"/> = <c>true</c>.</param>
    /// <returns>Сырое JSON-содержимое либо <c>null</c>, если источник не задан.</returns>
    /// <exception cref="TrackerException">
    /// Выбрасывается с <see cref="ErrorCode.InvalidArgs"/> при взаимном конфликте флагов,
    /// отсутствии файла, пустом stdin или невалидном JSON.
    /// </exception>
    public static string? Read(string? filePath, bool fromStdin, TextReader? stdinReader)
    {
        var hasFile = !string.IsNullOrWhiteSpace(filePath);

        if (hasFile && fromStdin)
        {
            throw new TrackerException(ErrorCode.InvalidArgs,
                "--json-file and --json-stdin are mutually exclusive.");
        }

        string content;
        if (hasFile)
        {
            if (!File.Exists(filePath))
            {
                throw new TrackerException(ErrorCode.InvalidArgs,
                    $"--json-file: file not found: {filePath}");
            }
            content = File.ReadAllText(filePath);
        }
        else if (fromStdin)
        {
            if (stdinReader is null)
            {
                throw new TrackerException(ErrorCode.InvalidArgs,
                    "--json-stdin: no stdin reader available.");
            }
            content = stdinReader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new TrackerException(ErrorCode.InvalidArgs,
                    "--json-stdin: stdin is empty.");
            }
        }
        else
        {
            return null;
        }

        try
        {
            using var _ = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new TrackerException(ErrorCode.InvalidArgs,
                "Invalid JSON in request body: " + ex.Message, inner: ex);
        }

        return content;
    }
}
