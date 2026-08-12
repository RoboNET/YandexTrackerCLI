namespace YandexTrackerCLI.Core.Config;

/// <summary>
/// Откуда взялся действующий список <c>allowed_write_issues</c>.
/// </summary>
/// <remarks>
/// Значение видно в выводе <c>yt auth status</c> (поле <c>allowed_write_issues_source</c>):
/// список может прийти из профиля, из <c>YT_ALLOWED_WRITE_ISSUES</c> или получиться
/// пересечением обоих, и оператору нужно уметь отличать эти случаи.
/// </remarks>
public enum WriteIssuesSource
{
    /// <summary>Список взят из профиля (или ограничения нет вовсе).</summary>
    Profile = 0,

    /// <summary>
    /// Список задан переменной <c>YT_ALLOWED_WRITE_ISSUES</c>; профиль ограничения не нёс,
    /// поэтому переменная только сузила область записи.
    /// </summary>
    Env = 1,

    /// <summary>
    /// Ограничения несли и профиль, и переменная; действует их пересечение.
    /// </summary>
    ProfileAndEnv = 2,
}
