namespace YandexTrackerCLI.Tests.PathResolution;

using TUnit.Core;
using YandexTrackerCLI.Core;

/// <summary>
/// Юнит-тесты <see cref="PathResolver.ResolveHome"/>: проверяем, что резолв уважает
/// env-overrides (HOME / USERPROFILE) на любой OS — это критично для CI на Windows-runner'е,
/// где <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> игнорирует
/// <c>USERPROFILE</c> и читает реестр.
/// Мутируют глобальные env-переменные, поэтому выполняются последовательно.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class PathResolverTests
{
    /// <summary>
    /// HOME имеет приоритет над USERPROFILE и над платформенным fallback'ом.
    /// </summary>
    [Test]
    public async Task ResolveHome_WhenHomeSet_ReturnsHome()
    {
        using var guard = new EnvScope();
        guard.Set("HOME", "/foo");
        guard.Set("USERPROFILE", "/bar");

        var actual = PathResolver.ResolveHome();

        await Assert.That(actual).IsEqualTo("/foo");
    }

    /// <summary>
    /// При пустом/незаданном HOME резолвер падает на USERPROFILE
    /// (важно для Windows-runner'а, где задан только USERPROFILE).
    /// </summary>
    [Test]
    public async Task ResolveHome_WhenHomeEmpty_FallsBackToUserProfile()
    {
        using var guard = new EnvScope();
        guard.Set("HOME", null);
        guard.Set("USERPROFILE", "/bar");

        var actual = PathResolver.ResolveHome();

        await Assert.That(actual).IsEqualTo("/bar");
    }

    /// <summary>
    /// Когда обе env-переменные пустые/не заданы, резолвер падает на платформенный
    /// <see cref="Environment.SpecialFolder.UserProfile"/>. Точное значение зависит от ОС,
    /// но не должно быть пустым и не должно бросать исключение.
    /// </summary>
    [Test]
    public async Task ResolveHome_WhenBothEmpty_UsesPlatformFallback()
    {
        using var guard = new EnvScope();
        guard.Set("HOME", null);
        guard.Set("USERPROFILE", null);

        var actual = PathResolver.ResolveHome();

        // Платформенный fallback может вернуть пустую строку в крайне ограниченных
        // chroot/sandbox окружениях, но ни при каких условиях не должен бросить.
        await Assert.That(actual).IsNotNull();
    }

    /// <summary>
    /// Пустая строка в HOME трактуется как "не задано" — иначе тест-фикстуры,
    /// которые сбрасывают переменную через <c>SetEnvironmentVariable(name, null)</c>,
    /// получали бы разное поведение в зависимости от версии runtime'а.
    /// </summary>
    [Test]
    public async Task ResolveHome_WhenHomeIsEmptyString_FallsBackToUserProfile()
    {
        using var guard = new EnvScope();
        guard.Set("HOME", string.Empty);
        guard.Set("USERPROFILE", "/bar");

        var actual = PathResolver.ResolveHome();

        await Assert.That(actual).IsEqualTo("/bar");
    }

    /// <summary>
    /// Минимальный backup/restore env-переменных — изолируем тест от внешнего окружения,
    /// не зависим от <see cref="TestEnv"/> (который тащит за собой YT_*-переменные).
    /// </summary>
    private sealed class EnvScope : IDisposable
    {
        private readonly Dictionary<string, string?> _backup = new();

        public void Set(string key, string? value)
        {
            if (!_backup.ContainsKey(key))
            {
                _backup[key] = Environment.GetEnvironmentVariable(key);
            }
            Environment.SetEnvironmentVariable(key, value);
        }

        public void Dispose()
        {
            foreach (var (k, v) in _backup)
            {
                Environment.SetEnvironmentVariable(k, v);
            }
        }
    }
}
