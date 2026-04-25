namespace YandexTrackerCLI.Output;

using System.Text;
using System.Text.Json;

/// <summary>
/// Хелперы для формирования success-маркеров команд в виде компактного JSON.
/// </summary>
/// <remarks>
/// Сборка идёт через <see cref="Utf8JsonWriter"/> (AOT-safe и выполняет корректное
/// экранирование кавычек, обратных слэшей и управляющих символов), чтобы downstream
/// JSON-парсеры (jq, AI-агенты и т.п.) не падали при необычных именах профилей/ключей.
/// </remarks>
public static class CommandOutput
{
    /// <summary>
    /// Записывает на <see cref="Console.Out"/> односвойственный JSON вида
    /// <c>{"&lt;key&gt;":"&lt;value&gt;"}</c> с корректным экранированием значения.
    /// Используется командами, выводящими success-маркер (например, <c>{"saved":"work"}</c>).
    /// </summary>
    /// <param name="key">Имя единственного JSON-поля. Также экранируется записью через writer.</param>
    /// <param name="value">Строковое значение поля; может содержать любые символы, включая
    /// кавычки, обратные слэши и управляющие символы.</param>
    public static void WriteSingleField(string key, string value)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString(key, value);
            w.WriteEndObject();
        }
        Console.Out.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
    }
}
