namespace YandexTrackerCLI.Interactive;

/// <summary>
/// Абстракция интерактивного UI-слоя поверх stderr: спиннеры, прогресс-индикаторы.
/// </summary>
/// <remarks>
/// Контракт:
/// <list type="bullet">
///   <item><description>Все декорации (спиннер, label) пишутся в <c>stderr</c>, чтобы
///         не загрязнять <c>stdout</c> (зарезервирован под bit-exact выход формата).</description></item>
///   <item><description>В режимах <c>Json</c>/<c>Minimal</c> и при перенаправлении
///         <c>stdout</c>/<c>stderr</c> используется <c>NoopInteractiveUI</c> — никаких ANSI-последовательностей,
///         никакого вывода. Это гарантирует bit-exact pipe-output для AI/script consumers.</description></item>
///   <item><description>Реализация должна быть AOT-friendly: никакого reflection и dynamic.</description></item>
/// </list>
/// </remarks>
public interface IInteractiveUI
{
    /// <summary>
    /// Выполняет асинхронную работу, отображая спиннер с указанным <paramref name="label"/>.
    /// </summary>
    /// <typeparam name="T">Тип результата.</typeparam>
    /// <param name="label">Начальный label рядом со спиннером.</param>
    /// <param name="work">Асинхронная работа; получает <see cref="IStatusContext"/> для обновления label.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результат <paramref name="work"/>.</returns>
    Task<T> Status<T>(string label, Func<IStatusContext, Task<T>> work, CancellationToken ct = default);

    /// <summary>
    /// Версия <see cref="Status{T}"/> без возвращаемого значения.
    /// </summary>
    /// <param name="label">Начальный label рядом со спиннером.</param>
    /// <param name="work">Асинхронная работа; получает <see cref="IStatusContext"/> для обновления label.</param>
    /// <param name="ct">Токен отмены.</param>
    Task Status(string label, Func<IStatusContext, Task> work, CancellationToken ct = default);

    /// <summary>
    /// <c>true</c> для Spectre-обёртки (рисует ANSI), <c>false</c> для no-op fallback.
    /// </summary>
    bool IsRich { get; }
}

/// <summary>
/// Контекст активного статус-индикатора, передаваемый в делегат <c>work</c>.
/// </summary>
public interface IStatusContext
{
    /// <summary>
    /// Обновляет текстовый label рядом со спиннером.
    /// </summary>
    /// <param name="label">Новый label.</param>
    void Update(string label);

    /// <summary>
    /// Меняет стиль спиннера. В <c>NoopInteractiveUI</c> — no-op.
    /// </summary>
    /// <param name="style">Стиль спиннера.</param>
    void Spinner(SpinnerStyle style);
}

/// <summary>
/// Стили спиннера, нейтральные относительно конкретной библиотеки. Маппятся на
/// <c>Spectre.Console.Spinner.Known.*</c> в <see cref="SpectreInteractiveUI"/>.
/// </summary>
public enum SpinnerStyle
{
    /// <summary>Дефолтный спиннер.</summary>
    Default,

    /// <summary>Точечный спиннер (dots).</summary>
    Dots,

    /// <summary>Звёздочка (star).</summary>
    Star,

    /// <summary>Прыгающая линия (line).</summary>
    Line,

    /// <summary>Часы (clock).</summary>
    Clock,
}
