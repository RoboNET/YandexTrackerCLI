namespace YandexTrackerCLI.Commands.Config;

using Core.Api.Errors;
using YandexTrackerCLI.Core.Config;

/// <summary>
/// Правило «<c>yt config set</c> может только ужесточить политику профиля».
/// </summary>
/// <remarks>
/// <para>
/// Политики профиля (<c>read_only</c>, <c>allowed_queues</c>, <c>allowed_write_issues</c>) —
/// это граница, которую утилита держит против собственного вызывающего. Если бы её снимала
/// та же утилита одной командой <c>yt config set read_only false</c>, границы бы не было:
/// вызывающий сам формирует командную строку. Поэтому <c>config set</c> пропускает только
/// ужесточение.
/// </para>
/// <para>
/// Единственный путь ослабить политику — завести профиль заново
/// (<c>yt auth login</c> в тот же профиль пересоздаёт его и берёт политики только из флагов
/// текущего вызова). Это работает как барьер, потому что <c>yt config get</c> маскирует
/// <c>auth.token</c> и <c>auth.private_key_pem</c>: без самих креденшелов повторный вход
/// не сделать.
/// </para>
/// <para>
/// Ограничение проверки: она держится, пока у вызывающего нет прямой записи в файл конфига.
/// Тот, кто может редактировать <c>config.json</c>, снимет любую политику — файл должен быть
/// закрыт правами файловой системы.
/// </para>
/// </remarks>
internal static class ConfigPolicyGuard
{
    /// <summary>
    /// Подсказка про путь сброса, добавляемая в каждое сообщение об отказе — иначе
    /// пользователь застревает без выхода.
    /// </summary>
    private const string ResetHint =
        "To reset profile policies, re-create the profile with `yt auth login` "
        + "(policies are taken from that invocation's flags only; credentials are required "
        + "and `yt config get` masks them).";

    /// <summary>
    /// Проверяет, что изменение профиля не ослабляет действующую политику.
    /// </summary>
    /// <param name="before">Профиль до изменения.</param>
    /// <param name="after">Профиль после применения <c>config set</c>.</param>
    /// <param name="profileName">Имя профиля (для сообщения об ошибке).</param>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.PolicyViolation"/>, если изменение снимает <c>read_only</c>
    /// либо расширяет/снимает <c>allowed_queues</c>/<c>allowed_write_issues</c>.
    /// </exception>
    public static void EnsureNotWeakened(Profile before, Profile after, string profileName)
    {
        if (before.ReadOnly && !after.ReadOnly)
        {
            throw new TrackerException(
                ErrorCode.PolicyViolation,
                $"read_only is enabled on profile '{profileName}' and cannot be turned off "
                + $"with `yt config set`. {ResetHint}");
        }

        EnsureListNotWidened(
            "allowed_queues",
            QueuePolicy.Normalize(before.AllowedQueues),
            QueuePolicy.Normalize(after.AllowedQueues),
            profileName);

        EnsureListNotWidened(
            "allowed_write_issues",
            IssueWritePolicy.Normalize(before.AllowedWriteIssues),
            IssueWritePolicy.Normalize(after.AllowedWriteIssues),
            profileName);
    }

    /// <summary>
    /// Проверяет, что список-ограничение не был снят и не был расширен: если ограничение
    /// действует, новый список обязан быть непустым подмножеством текущего.
    /// </summary>
    /// <param name="key">Имя ключа конфига (для сообщения).</param>
    /// <param name="before">Нормализованный текущий список.</param>
    /// <param name="after">Нормализованный новый список.</param>
    /// <param name="profileName">Имя профиля.</param>
    /// <exception cref="TrackerException"><see cref="ErrorCode.PolicyViolation"/>.</exception>
    private static void EnsureListNotWidened(
        string key,
        string[] before,
        string[] after,
        string profileName)
    {
        if (before.Length == 0)
        {
            // Ограничения нет — установить любой список можно, это ужесточение.
            return;
        }

        if (after.Length == 0)
        {
            throw new TrackerException(
                ErrorCode.PolicyViolation,
                $"{key} is set on profile '{profileName}' (current: {string.Join(", ", before)}) "
                + $"and cannot be cleared with `yt config set`; only narrowing is allowed. {ResetHint}");
        }

        var current = new HashSet<string>(before, StringComparer.OrdinalIgnoreCase);
        var added = after.Where(v => !current.Contains(v)).ToArray();
        if (added.Length > 0)
        {
            throw new TrackerException(
                ErrorCode.PolicyViolation,
                $"{key} on profile '{profileName}' can only be narrowed: "
                + $"{string.Join(", ", added)} is outside the current list "
                + $"({string.Join(", ", before)}). {ResetHint}");
        }
    }
}
