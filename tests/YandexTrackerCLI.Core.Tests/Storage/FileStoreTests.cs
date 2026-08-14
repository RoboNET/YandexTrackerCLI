namespace YandexTrackerCLI.Core.Tests.Storage;

using TUnit.Core;
using YandexTrackerCLI.Core.Storage;

/// <summary>
/// Повтор завершающего переименования. Платформа решает только, включён ли повтор, —
/// сама логика проверяется на любой ОС, поэтому тесты идут и на macOS/Linux.
/// </summary>
public sealed class FileStoreTests
{
    private const int HResultSharingViolation = unchecked((int)0x80070020);

    /// <summary>
    /// Замена целевого файла, отказавшая пару раз преходящей ошибкой (файл на мгновение
    /// держал антивирус, индексатор или другой писатель), обязана в итоге пройти.
    /// </summary>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task CommitWithRetry_TransientFailures_EventuallySucceed(int failures)
    {
        var attempts = 0;

        await FileStore.CommitWithRetry(
            () =>
            {
                attempts++;
                if (attempts <= failures)
                {
                    throw new UnauthorizedAccessException("Access to the path is denied.");
                }
            },
            retryTransientFailures: true,
            CancellationToken.None);

        await Assert.That(attempts).IsEqualTo(failures + 1);
    }

    /// <summary>
    /// Нарушение совместного доступа Windows приходит <see cref="IOException"/> с кодом,
    /// а не <see cref="UnauthorizedAccessException"/> — оно тоже преходящее.
    /// </summary>
    [Test]
    public async Task CommitWithRetry_SharingViolation_IsRetried()
    {
        var attempts = 0;

        await FileStore.CommitWithRetry(
            () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new IOException("The process cannot access the file.", HResultSharingViolation);
                }
            },
            retryTransientFailures: true,
            CancellationToken.None);

        await Assert.That(attempts).IsEqualTo(2);
    }

    /// <summary>
    /// Повтор ограничен: бесконечно висеть на отказе, который повтором не лечится, нельзя.
    /// Наружу выходит последнее исходное исключение — обёртку в <c>config_error</c>
    /// делает вызывающий код.
    /// </summary>
    [Test]
    public async Task CommitWithRetry_WhenFailuresPersist_SurfacesOriginalException()
    {
        var attempts = 0;

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await FileStore.CommitWithRetry(
                () =>
                {
                    attempts++;
                    throw new UnauthorizedAccessException("denied");
                },
                retryTransientFailures: true,
                CancellationToken.None));

        await Assert.That(ex!.Message).IsEqualTo("denied");
        // Одна первая попытка плюс ограниченное число повторов, а не бесконечность.
        await Assert.That(attempts).IsGreaterThan(1);
        await Assert.That(attempts).IsLessThanOrEqualTo(8);
    }

    /// <summary>
    /// На POSIX повтор выключен: <c>rename(2)</c> атомарен, и отказ там честный.
    /// </summary>
    [Test]
    public async Task CommitWithRetry_WhenDisabled_FailsOnFirstAttempt()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await FileStore.CommitWithRetry(
                () =>
                {
                    attempts++;
                    throw new UnauthorizedAccessException("denied");
                },
                retryTransientFailures: false,
                CancellationToken.None));

        await Assert.That(attempts).IsEqualTo(1);
    }

    /// <summary>
    /// Отказ, который повтором не лечится (нет каталога), не должен съедать паузы.
    /// </summary>
    [Test]
    public async Task CommitWithRetry_NonTransientFailure_IsNotRetried()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
            await FileStore.CommitWithRetry(
                () =>
                {
                    attempts++;
                    throw new DirectoryNotFoundException("no such directory");
                },
                retryTransientFailures: true,
                CancellationToken.None));

        await Assert.That(attempts).IsEqualTo(1);
    }
}
