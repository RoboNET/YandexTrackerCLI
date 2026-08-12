namespace YandexTrackerCLI.Core.Http;

/// <summary>
/// Разбор уже собранного URL запроса для guard-хендлеров: путь, query и
/// percent-decoding сегментов.
/// </summary>
/// <remarks>
/// Команды экранируют ключи через <see cref="Uri.EscapeDataString"/>, поэтому сегменты
/// приходят закодированными; сравнение ключей политиками выполняется только после
/// декодирования — иначе <c>%4FPS-1</c> обходил бы проверку.
/// </remarks>
internal static class RequestUriPath
{
    /// <summary>
    /// Делит URI на путь и query, корректно обрабатывая относительный URI
    /// (для него <see cref="Uri.AbsolutePath"/> бросает исключение).
    /// </summary>
    /// <param name="uri">URI запроса.</param>
    /// <returns>Путь и query (query — с ведущим <c>?</c> либо пустая строка).</returns>
    public static (string Path, string Query) Split(Uri uri)
    {
        if (uri.IsAbsoluteUri)
        {
            return (uri.AbsolutePath, uri.Query);
        }

        var raw = uri.OriginalString;
        var q = raw.IndexOf('?');
        return q < 0 ? (raw, string.Empty) : (raw[..q], raw[q..]);
    }

    /// <summary>
    /// Возвращает непустые сегменты пути (без декодирования).
    /// </summary>
    /// <param name="path">Путь URI.</param>
    /// <returns>Массив сегментов.</returns>
    public static string[] Segments(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Снимает percent-encoding с сегмента пути или параметра query.
    /// </summary>
    /// <param name="value">Исходное значение.</param>
    /// <returns>
    /// Декодированное значение; при некорректной escape-последовательности — исходная строка,
    /// чтобы «сломанный» ключ всё равно попал в сравнение (и при действующей политике был отклонён).
    /// </returns>
    public static string Decode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }
}
