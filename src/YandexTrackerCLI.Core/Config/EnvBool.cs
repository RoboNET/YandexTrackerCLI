namespace YandexTrackerCLI.Core.Config;

using Api.Errors;

/// <summary>
/// Результат классификации булева значения переменной окружения.
/// </summary>
public enum EnvBoolValue
{
    /// <summary>Переменная не задана, пуста или состоит из одних пробелов.</summary>
    Unset,

    /// <summary>Распознанное истинное написание (<c>1</c>, <c>true</c>, <c>yes</c>, <c>on</c>).</summary>
    True,

    /// <summary>Распознанное ложное написание (<c>0</c>, <c>false</c>, <c>no</c>, <c>off</c>).</summary>
    False,

    /// <summary>Значение непустое, но ни на что не похоже (<c>enabled</c>, <c>да</c>, <c>maybe</c>).</summary>
    Unrecognized,
}

/// <summary>
/// Единственное место, где разбираются булевы значения переменных окружения.
/// Два режима с разной семантикой отказа:
/// <see cref="Classify"/> + <see cref="ResolveStrict"/> — строгий разбор для переменных,
/// которые управляют защитой, и <see cref="IsTruthyLenient"/> — мягкий для переменных,
/// которые лишь включают необязательную возможность.
/// </summary>
/// <remarks>
/// <para>
/// Разница не косметическая. Критерий выбора режима — цена неверно разобранного значения.
/// </para>
/// <para>
/// Строгий разбор (<see cref="ResolveStrict"/>) нужен всем переменным, от значения которых
/// зависит защита, — и тем, что её включают, и тем, что её снимают. <c>YT_READ_ONLY</c>
/// существует ровно затем, чтобы ограничивать: человек, написавший <c>YT_READ_ONLY=on</c>,
/// считает себя защищённым и пойдёт писать в Трекер, если мы тихо сочтём значение
/// «выключено». <c>YT_LOG_RAW</c> — зеркальный случай: она СНИМАЕТ маскирование секретов
/// в wire-log, поэтому нераспознанное значение не должно её включать, иначе
/// <c>YT_LOG_RAW=disabled</c> положит в файл живые <c>Authorization</c>, <c>DPoP</c> и
/// <c>refresh_token</c>. В обоих случаях мусор — ошибка конфигурации
/// (<see cref="ErrorCode.ConfigError"/>), а не молчаливое решение за пользователя.
/// </para>
/// <para>
/// Мягкий разбор (<see cref="IsTruthyLenient"/>) остаётся только у переменных, которые
/// включают необязательную возможность и ничего не снимают, — например <c>YT_HYPERLINKS</c>:
/// цена ошибки там — лишняя ссылка в выводе.
/// </para>
/// </remarks>
public static class EnvBool
{
    /// <summary>Написания, означающие «включено».</summary>
    private static readonly string[] TrueLiterals = ["1", "true", "yes", "on"];

    /// <summary>Написания, означающие «выключено».</summary>
    private static readonly string[] FalseLiterals = ["0", "false", "no", "off"];

    /// <summary>
    /// Классифицирует сырое значение переменной окружения: пробелы по краям обрезаются,
    /// сравнение регистронезависимое.
    /// </summary>
    /// <param name="raw">Сырое значение переменной (может быть <see langword="null"/>).</param>
    /// <returns>Одно из состояний <see cref="EnvBoolValue"/>.</returns>
    public static EnvBoolValue Classify(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return EnvBoolValue.Unset;
        }

        var v = raw.Trim();

        foreach (var t in TrueLiterals)
        {
            if (string.Equals(v, t, StringComparison.OrdinalIgnoreCase))
            {
                return EnvBoolValue.True;
            }
        }

        foreach (var f in FalseLiterals)
        {
            if (string.Equals(v, f, StringComparison.OrdinalIgnoreCase))
            {
                return EnvBoolValue.False;
            }
        }

        return EnvBoolValue.Unrecognized;
    }

    /// <summary>
    /// Строго разбирает значение переменной окружения, управляющей защитой,
    /// беря сырое значение из snapshot'а окружения.
    /// </summary>
    /// <param name="env">Snapshot переменных окружения.</param>
    /// <param name="key">Имя переменной.</param>
    /// <param name="fallbackHint">Хвост сообщения об ошибке, объясняющий, что действует,
    /// если переменную снять (например, «the profile's <c>read_only</c>»).</param>
    /// <returns><see langword="true"/> — переменная явно включена; <see langword="false"/> —
    /// переменная не задана либо явно выключена.</returns>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.ConfigError"/> — значение непустое, но не является булевым.
    /// </exception>
    public static bool ResolveStrict(
        IReadOnlyDictionary<string, string?> env,
        string key,
        string fallbackHint) =>
        ResolveStrict(env.GetValueOrDefault(key), key, fallbackHint);

    /// <summary>
    /// Строго разбирает сырое значение переменной окружения, управляющей защитой.
    /// Включено только явное истинное написание; всё нераспознанное — ошибка конфигурации,
    /// чтобы мусор никогда не решал за пользователя, действует защита или нет.
    /// </summary>
    /// <param name="raw">Сырое значение переменной (может быть <see langword="null"/>).</param>
    /// <param name="key">Имя переменной — попадает в сообщение об ошибке.</param>
    /// <param name="fallbackHint">Хвост сообщения об ошибке, объясняющий, что действует,
    /// если переменную снять (например, «the profile's <c>read_only</c>»).</param>
    /// <returns><see langword="true"/> — переменная явно включена; <see langword="false"/> —
    /// переменная не задана либо явно выключена.</returns>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.ConfigError"/> — значение непустое, но не является булевым.
    /// </exception>
    public static bool ResolveStrict(string? raw, string key, string fallbackHint)
    {
        return Classify(raw) switch
        {
            EnvBoolValue.True  => true,
            EnvBoolValue.False => false,
            EnvBoolValue.Unset => false,
            _ => throw new TrackerException(
                ErrorCode.ConfigError,
                $"{key} is set to '{raw!.Trim()}', which is not a boolean value. "
                + $"Use one of {string.Join("/", TrueLiterals)} to enable "
                + $"or {string.Join("/", FalseLiterals)} to disable (case-insensitive, surrounding "
                + $"whitespace ignored), or unset the variable to fall back to {fallbackHint}."),
        };
    }

    /// <summary>
    /// Мягкий разбор в духе общей договорённости для env-флагов: включено всё, что непусто
    /// и не является явным отрицанием (<c>0</c>/<c>false</c>/<c>no</c>/<c>off</c>).
    /// </summary>
    /// <param name="raw">Сырое значение переменной (может быть <see langword="null"/>).</param>
    /// <returns><see langword="true"/>, если значение следует считать истинным.</returns>
    /// <remarks>
    /// Применять только к переменным, которые ВКЛЮЧАЮТ необязательную возможность и ничего
    /// не ограничивают и не снимают (<c>YT_HYPERLINKS</c>). Для всего, от чего зависит
    /// защита, используйте <see cref="ResolveStrict(string?, string, string)"/>.
    /// </remarks>
    public static bool IsTruthyLenient(string? raw) =>
        Classify(raw) is not (EnvBoolValue.Unset or EnvBoolValue.False);
}
