namespace YandexTrackerCLI.Output;

/// <summary>
/// Формат вывода CLI: режим, в котором сериализуется ответ API на stdout.
/// </summary>
/// <remarks>
/// Значение <see cref="Auto"/> резолвится в один из конкретных форматов
/// (<see cref="Json"/>, <see cref="Minimal"/>, <see cref="Table"/>) на входной точке
/// через <c>FormatResolver</c>: cascade (CLI → env <c>YT_FORMAT</c> → profile
/// <c>default_format</c> → TTY-detect). До выхода в <c>JsonWriter.Write</c>
/// формат уже не должен быть <see cref="Auto"/>.
/// </remarks>
public enum OutputFormat
{
    /// <summary>
    /// Sentinel значение: формат ещё не выбран, требует резолвинга через cascade.
    /// </summary>
    Auto,

    /// <summary>
    /// Сырой JSON (compact или pretty в зависимости от <c>pretty</c>-флага).
    /// </summary>
    Json,

    /// <summary>
    /// Краткий машиночитаемый формат: одно identifying-поле на строку.
    /// </summary>
    Minimal,

    /// <summary>
    /// Человекочитаемая таблица для TTY (key-value для одиночной сущности,
    /// многоколоночная — для массивов).
    /// </summary>
    Table,
}
