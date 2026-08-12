namespace YandexTrackerCLI.Core.Http;

/// <summary>
/// Классификация HTTP-запроса как мутирующего — общая для всех политик записи
/// (<c>read_only</c>, <c>allowed_write_issues</c>), чтобы исключения совпадали.
/// </summary>
internal static class MutatingRequest
{
    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE",
    };

    /// <summary>
    /// Определяет, меняет ли метод состояние на сервере.
    /// </summary>
    /// <param name="request">Исходящий запрос.</param>
    /// <returns><c>true</c> для POST/PUT/PATCH/DELETE.</returns>
    public static bool IsMutating(HttpRequestMessage request) =>
        MutatingMethods.Contains(request.Method.Method);

    /// <summary>
    /// Определяет, является ли запрос обращением к search-эндпоинту Яндекс Трекера,
    /// который использует <c>POST</c> семантически как чтение (например,
    /// <c>/v3/issues/_search</c>, <c>/v3/entities/project/_search</c>): такой вызов
    /// несёт запрос в теле, но состояние не меняет.
    /// </summary>
    /// <param name="request">Исходящий запрос.</param>
    /// <returns><c>true</c>, если это <c>POST</c> на путь, оканчивающийся на <c>/_search</c>.</returns>
    public static bool IsSafePostSearch(HttpRequestMessage request)
    {
        if (!HttpMethod.Post.Equals(request.Method) || request.RequestUri is null)
        {
            return false;
        }

        var (path, _) = RequestUriPath.Split(request.RequestUri);
        return path.EndsWith("/_search", StringComparison.Ordinal);
    }
}
