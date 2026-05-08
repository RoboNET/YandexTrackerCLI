namespace YandexTrackerCLI.Skill;

/// <summary>
/// Тип события progress'а во время skill install/update.
/// </summary>
public enum SkillProgressKind
{
    /// <summary>Запись началась.</summary>
    Started,

    /// <summary>Файл успешно записан.</summary>
    Wrote,

    /// <summary>Локация пропущена (например, неподдерживаемая комбинация).</summary>
    Skipped,

    /// <summary>Запись завершилась ошибкой.</summary>
    Failed,
}

/// <summary>
/// Событие progress'а одной (target, scope) пары в <see cref="SkillManager.Update(System.Collections.Generic.IReadOnlyCollection{SkillTarget}, System.Collections.Generic.IReadOnlyCollection{SkillScope}, string, System.IProgress{SkillProgressEvent}?)"/>.
/// </summary>
/// <param name="Target">Целевой ассистент.</param>
/// <param name="Scope">Зона.</param>
/// <param name="Path">Путь до файла на диске.</param>
/// <param name="Kind">Тип события.</param>
/// <param name="Version">Записанная версия (для <see cref="SkillProgressKind.Wrote"/>); иначе <c>null</c>.</param>
/// <param name="Error">Сообщение ошибки (для <see cref="SkillProgressKind.Failed"/>); иначе <c>null</c>.</param>
public readonly record struct SkillProgressEvent(
    SkillTarget Target,
    SkillScope Scope,
    string Path,
    SkillProgressKind Kind,
    string? Version,
    string? Error);
