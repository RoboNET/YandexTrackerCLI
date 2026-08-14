namespace YandexTrackerCLI.Core.Storage;

using System.Runtime.InteropServices;
using Api.Errors;

/// <summary>
/// Общая механика файловых хранилищ CLI (конфиг, кэш IAM-токенов): атомарная запись
/// через уникальный временный файл с правами владельца и кросс-процессный файловый лок
/// для сериализации read-modify-write.
/// </summary>
/// <remarks>
/// Вынесено в одно место намеренно: два хранилища с одинаковыми требованиями к правам
/// и атомарности расходились бы при первой же правке одного из них.
/// </remarks>
internal static class FileStore
{
    private static readonly TimeSpan LockPollInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Берёт эксклюзивный лок на отдельном файле <paramref name="lockPath"/>, повторяя
    /// попытки до истечения <paramref name="timeout"/>.
    /// </summary>
    /// <param name="lockPath">
    /// Путь к lock-файлу. Это должен быть отдельный файл рядом с данными, а не сам файл
    /// данных: запись коммитится через <see cref="File.Move(string, string, bool)"/>, и лок,
    /// взятый на старом inode, после переименования уже ничего не защищает.
    /// </param>
    /// <param name="timeout">Максимальное время ожидания лока.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>
    /// <see cref="FileStream"/>, удерживающий лок; освобождается его освобождением
    /// (<c>Dispose</c>). Сам lock-файл при этом не удаляется — удаление открыло бы окно,
    /// в котором два процесса держат локи на разных inode'ах под одним именем.
    /// </returns>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.ConfigError"/> — лок не удалось взять за отведённое время.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Лок <b>рекомендательный</b>: на Unix .NET реализует <see cref="FileShare"/> через
    /// <c>flock(2)</c>, который не мешает постороннему процессу открыть и переписать файл,
    /// проигнорировав лок. Нам этого достаточно — в эти файлы пишет только сам <c>yt</c>,
    /// и защищаемся мы от собственных параллельных запусков, а не от чужого процесса.
    /// </para>
    /// <para>
    /// На сетевых ФС эксклюзивность может молча выродиться: на SMB/CIFS и в части
    /// конфигураций NFS (без работающего <c>lockd</c>/<c>flock</c>-эмуляции) захват удаётся
    /// обоим процессам сразу. Тогда возвращается потеря обновлений — тот, кто записал
    /// вторым, затирает правку первого, — но порчу файла это не возвращает: коммит идёт
    /// через <see cref="WriteAtomic"/>, то есть уникальный временный файл и переименование.
    /// </para>
    /// </remarks>
    public static async Task<FileStream> AcquireLock(string lockPath, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            EnsureDirectory(lockPath);

            var deadline = DateTime.UtcNow + timeout;
            IOException? last = null;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return new FileStream(lockPath, LockFileOptions());
                }
                catch (IOException ex) when (IsRetriable(ex))
                {
                    // Лок держит другой процесс (или другой FileStream этого же процесса).
                    last = ex;
                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new TrackerException(
                            ErrorCode.ConfigError,
                            $"Timed out after {timeout.TotalSeconds:0.#}s waiting for the lock on '{lockPath}'. "
                            + "Another yt process is writing to this file; retry once it finishes. "
                            + $"Last error while acquiring the lock: {ex.Message}",
                            inner: last);
                    }

                    await Task.Delay(LockPollInterval, ct);
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            // Каталог конфига только для чтения (или сам lock-файл чужой). Команды ловят
            // только TrackerException, так что без обёртки пользователь получил бы
            // необработанное исключение со стектрейсом вместо структурной ошибки.
            throw new TrackerException(
                ErrorCode.ConfigError,
                $"Cannot create or open the lock file '{lockPath}': {ex.Message}",
                inner: ex);
        }
    }

    /// <summary>
    /// Отличает «файл занят другим держателем лока» от отказов, которые повтором не лечатся.
    /// </summary>
    /// <param name="ex">Исключение, полученное при открытии lock-файла.</param>
    /// <returns><c>true</c>, если попытку имеет смысл повторить.</returns>
    /// <remarks>
    /// Крутить весь таймаут на удалённом каталоге конфига или на слишком длинном пути
    /// бессмысленно, а диагноз «другой процесс yt пишет в файл» там ещё и ложный. Полный
    /// <see cref="IOException"/> (кончившееся место, ошибка сетевой ФС) от нарушения
    /// совместного доступа переносимо не отличается, поэтому такие случаи всё же
    /// повторяются — но исходное исключение прикладывается к ошибке таймаута.
    /// </remarks>
    private static bool IsRetriable(IOException ex) =>
        ex is not (DirectoryNotFoundException or FileNotFoundException or PathTooLongException);

    /// <summary>
    /// Атомарно записывает файл: пишет в уникальный временный файл рядом, выставляет
    /// права владельца (0600) на POSIX и переименовывает поверх целевого пути.
    /// </summary>
    /// <param name="path">Целевой путь.</param>
    /// <param name="write">Колбэк, пишущий содержимое в поток временного файла.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <remarks>
    /// <para>
    /// Имя временного файла уникально для каждой записи: фиксированное <c>.tmp</c> означало бы,
    /// что вторая одновременная запись усекает наполовину записанный файл первой, а результатом
    /// был бы обрезанный файл с креденшелами. При ошибке записи временный файл удаляется,
    /// чтобы мусор не копился рядом с конфигом.
    /// </para>
    /// <para>
    /// Данные сбрасываются на диск (<c>Flush(flushToDisk: true)</c>) <b>до</b> переименования.
    /// Без этого порядок «данные раньше метаданных» держался бы на эвристиках ФС: ext4
    /// подстраховывает авто-fsync при rename, APFS и прочие такой гарантии не дают. Цена
    /// пропуска — авария сразу после переименования оставляет пустой файл на месте уже
    /// отвязанного старого inode, то есть потерю креденшелов всех профилей разом.
    /// </para>
    /// </remarks>
    public static async Task WriteAtomic(string path, Func<Stream, CancellationToken, Task> write, CancellationToken ct)
    {
        string? tmp = null;
        try
        {
            EnsureDirectory(path);

            tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var fs = new FileStream(tmp, TempFileOptions()))
            {
                await write(fs, ct);
                await fs.FlushAsync(ct);
                fs.Flush(flushToDisk: true);
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // При создании режим уже задан (UnixCreateMode), но он проходит через umask
                // процесса и может недосчитаться битов владельца. Повторная установка делает
                // права детерминированными независимо от umask.
                File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(tmp, path, overwrite: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            if (tmp is not null)
            {
                TryDelete(tmp);
            }

            // Каталог только для чтения либо файл принадлежит другому пользователю: наружу
            // это должно выйти как config_error, а не как необработанное исключение.
            throw new TrackerException(
                ErrorCode.ConfigError,
                $"Cannot write '{path}': {ex.Message}",
                inner: ex);
        }
        catch
        {
            if (tmp is not null)
            {
                TryDelete(tmp);
            }

            throw;
        }
    }

    /// <summary>
    /// Открывает файл на чтение так, чтобы конкурентная запись другого процесса не падала.
    /// </summary>
    /// <param name="path">Путь к файлу.</param>
    /// <returns>Поток для чтения.</returns>
    /// <remarks>
    /// <see cref="FileShare.Delete"/> здесь обязателен: запись коммитится переименованием
    /// поверх целевого пути, а на Windows это переименование падает нарушением совместного
    /// доступа, если файл открыт без разрешения на удаление. Иначе конкурентный
    /// <b>читатель</b> ронял бы чужую запись.
    /// </remarks>
    public static FileStream OpenSharedRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static FileStreamOptions LockFileOptions()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
        };

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Lock-файл лежит рядом с конфигом (0600) — не ослабляем права каталога соседством.
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }

    private static FileStreamOptions TempFileOptions()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Права выставляются в момент создания: между созданием файла и chmod иначе
            // существует окно, в котором токены читаемы всей группе.
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            /* best effort */
        }
        catch (UnauthorizedAccessException)
        {
            /* best effort */
        }
    }
}
