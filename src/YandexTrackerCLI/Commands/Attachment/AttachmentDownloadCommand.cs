namespace YandexTrackerCLI.Commands.Attachment;

using System.Buffers;
using System.CommandLine;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt attachment download &lt;issue-key&gt; &lt;attachment-id&gt;
/// [--out &lt;path&gt;|-] [--force]</c>: скачивает вложение через
/// <c>GET /v3/issues/{key}/attachments/{id}/download</c> со стримингом тела ответа
/// в файл или в stdout.
/// </summary>
/// <remarks>
/// <para>
/// Целевой путь определяется так:
/// <list type="number">
///   <item><description>
///     <c>--out -</c> (ровно один дефис) — тело ответа стримится в raw-stdout,
///     файл на диск не пишется.
///   </description></item>
///   <item><description>Если задан <c>--out &lt;path&gt;</c> — используется напрямую.</description></item>
///   <item><description>
///     Иначе берётся имя файла из заголовка <c>Content-Disposition</c>
///     (<see cref="Core.Api.TrackerDownload.FileName"/>); fallback —
///     <c>attachment-&lt;id&gt;</c>. Файл пишется в текущую рабочую директорию
///     (<see cref="Directory.GetCurrentDirectory"/>).
///   </description></item>
/// </list>
/// </para>
/// <para>
/// Если целевой файл уже существует и <c>--force</c> не указан — возвращается
/// <see cref="ErrorCode.InvalidArgs"/> (exit 2). С <c>--force</c> файл перезаписывается.
/// По завершении на stdout пишется JSON вида <c>{"downloaded":"&lt;path&gt;","bytes":&lt;n&gt;}</c>.
/// </para>
/// <para>
/// В режиме <c>--out -</c> stdout содержит только байты вложения: сводка пишется
/// в stderr строкой <c>downloaded &lt;n&gt; bytes</c>. Комбинация <c>--out - --force</c>
/// бессмысленна и отвергается как <see cref="ErrorCode.InvalidArgs"/> (exit 2).
/// Обрыв пайпа потребителем (например <c>| head -c 100</c>) — единственный штатный
/// сценарий досрочной остановки: копирование прекращается, в stderr пишется
/// <c>downloaded &lt;n&gt; bytes (truncated: consumer closed the pipe)</c>, exit 0.
/// Любая другая ошибка — обрыв тела HTTP-ответа, ошибка записи (например ENOSPC)
/// или тело короче объявленного <c>Content-Length</c> — это
/// <see cref="ErrorCode.NetworkError"/> (exit 8): усечённые данные никогда
/// не выдаются потребителю за успех.
/// </para>
/// <para>
/// При записи в файл действует то же правило, и держится оно не уборкой после сбоя,
/// а порядком операций: вложение пишется во временный файл
/// <c>&lt;target&gt;.part-&lt;random&gt;</c> в каталоге цели и переезжает под целевое имя
/// (<see cref="File.Move(string, string, bool)"/>) только после успешной проверки
/// <c>Content-Length</c>. Поэтому файл под целевым именем всегда означает полностью
/// скачанное вложение — в том числе после SIGKILL или отключения питания, когда никакой
/// обработчик уже не сработает, — а <c>--force</c> уничтожает прежнее содержимое ровно
/// в момент успеха, а не первым же полученным байтом. Оборванное скачивание (отмена,
/// exit 11; ошибка I/O или тело короче <c>Content-Length</c>, exit 8) удаляет свой временный
/// файл и пишет в stderr <c>removed incomplete download: &lt;tmp&gt;</c>.
/// В режиме <c>--out -</c> удалять нечего: отданные в пайп байты уже у потребителя,
/// и признаком неполноты служит exit-код.
/// </para>
/// <para>
/// Схема требует права записи в каталог цели: если писать можно только в сам файл,
/// команда падает с <see cref="ErrorCode.NetworkError"/> (exit 8), не тронув цель.
/// Симлинк разыменовывается — обновляется файл по ссылке, а не подменяется ссылка.
/// Специальные приёмники (<c>/dev/null</c>, <c>/dev/tty</c>, DOS-устройства вроде <c>NUL</c>)
/// не переименовываются: в них пишем напрямую и ничего не удаляем.
/// </para>
/// </remarks>
public static class AttachmentDownloadCommand
{
    /// <summary>
    /// Тестовая подмена raw-stdout потока для режима <c>--out -</c>
    /// (в проде используется <see cref="Console.OpenStandardOutput()"/>).
    /// </summary>
    internal static readonly AsyncLocal<Stream?> TestStdoutOverride = new();

    /// <summary>
    /// Строит subcommand <c>download</c> для <c>yt attachment</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("issue-key") { Description = "Ключ задачи (например DEV-1)." };
        var idArg = new Argument<string>("attachment-id") { Description = "Идентификатор вложения." };
        var outOpt = new Option<string?>("--out")
        {
            Description = "Явный путь для сохранения файла; \"-\" — писать вложение в stdout.",
        };
        var forceOpt = new Option<bool>("--force")
        {
            Description = "Перезаписать существующий файл.",
        };

        var cmd = new Command(
            "download",
            "Скачать вложение (GET /v3/issues/{key}/attachments/{id}/download).");
        cmd.Arguments.Add(keyArg);
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(outOpt);
        cmd.Options.Add(forceOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                var id = pr.GetValue(idArg)!;
                var outPath = pr.GetValue(outOpt);
                var force = pr.GetValue(forceOpt);
                var toStdout = outPath == "-";

                if (toStdout && force)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        "--force has no meaning with --out -: stdout is never overwritten");
                }

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                await using var download = await ctx.Client.GetStreamingAsync(
                    $"issues/{Uri.EscapeDataString(key)}/attachments/{Uri.EscapeDataString(id)}/download",
                    ct);

                if (toStdout)
                {
                    // Поток намеренно не диспозится: Console.OpenStandardOutput() отдаёт
                    // небуферизованный поток поверх дублированного дескриптора, всё
                    // содержимое уже отдано FlushAsync, а fd 1 закроет выход из процесса.
                    // Один путь и в проде, и в тестах — подменять нечему расходиться.
                    var stdout = TestStdoutOverride.Value ?? Console.OpenStandardOutput();
                    var (copied, pipeBroken) = await CopyToStdout(download.Stream, stdout, ct);

                    if (pipeBroken)
                    {
                        ErrorStream(pr).WriteLine(
                            $"downloaded {copied} bytes (truncated: consumer closed the pipe)");
                        return 0;
                    }

                    if (download.ContentLength is { } expected && copied < expected)
                    {
                        throw new TrackerException(
                            ErrorCode.NetworkError,
                            $"truncated download: expected {expected} bytes, got {copied}");
                    }

                    ErrorStream(pr).WriteLine($"downloaded {copied} bytes");
                    return 0;
                }

                string target;
                if (!string.IsNullOrWhiteSpace(outPath))
                {
                    target = outPath;
                }
                else
                {
                    var candidate = string.IsNullOrWhiteSpace(download.FileName)
                        ? $"attachment-{id}"
                        : download.FileName!;
                    // Content-Disposition can carry an attacker-controlled filename
                    // (including path traversal like "../../etc/passwd"). Strip every
                    // directory component so we only use the final file-name segment,
                    // and fall back to a safe default if nothing remains after stripping.
                    var name = Path.GetFileName(candidate);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = $"attachment-{id}";
                    }

                    target = Path.Combine(Directory.GetCurrentDirectory(), name);
                }

                if (File.Exists(target) && !force)
                {
                    throw new TrackerException(
                        ErrorCode.InvalidArgs,
                        $"file exists, use --force: {target}");
                }

                var stderr = ErrorStream(pr);
                var bytes = await WriteToFile(download, target, force, stderr, ct);

                using var ms = new MemoryStream();
                using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
                {
                    w.WriteStartObject();
                    w.WriteString("downloaded", target);
                    w.WriteNumber("bytes", bytes);
                    w.WriteEndObject();
                }

                Console.Out.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
                return 0;
            }
            catch (TrackerException ex)
            {
                ErrorWriter.Write(ErrorStream(pr), ex);
                return ex.Code.ToExitCode();
            }
        });
        return cmd;
    }

    /// <summary>
    /// Возвращает writer stderr, сконфигурированный для этого вызова.
    /// </summary>
    /// <param name="pr">Результат разбора аргументов.</param>
    /// <returns>Writer stderr вызова либо <see cref="Console.Error"/>, если конфигурации нет.</returns>
    /// <remarks>
    /// Пояснительные строки команды обязаны идти туда же, куда идёт её JSON-ошибка, —
    /// иначе in-process вызов (тесты, встраивание) получает половину вывода мимо своих
    /// writer'ов, в настоящий stderr процесса.
    /// </remarks>
    private static TextWriter ErrorStream(ParseResult pr) => pr.InvocationConfiguration.Error;

    /// <summary>
    /// Скачивает вложение в файл через временный файл рядом с целью и атомарное переименование.
    /// </summary>
    /// <param name="download">Открытый ответ с телом вложения.</param>
    /// <param name="target">Целевой путь.</param>
    /// <param name="force">Разрешено ли перезаписать существующий файл.</param>
    /// <param name="stderr">Writer для пояснительных строк.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Количество записанных байт.</returns>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.NetworkError"/> — тело короче <c>Content-Length</c> либо локальная
    /// ошибка записи; <see cref="ErrorCode.InvalidArgs"/> — целевой файл появился между
    /// проверкой и переименованием, а <c>--force</c> не задан.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Инвариант «файл под целевым именем = полностью скачанное вложение» держится не уборкой
    /// после сбоя, а тем, что до успешной проверки размера целевого имени вообще не существует.
    /// Это верно и при жёстком убийстве процесса (SIGKILL, отключение питания), когда никакой
    /// обработчик сигналов уже не поможет, и оставляет прежнее содержимое файла нетронутым при
    /// <c>--force</c>: оно уничтожается ровно в момент успешного переименования.
    /// </para>
    /// <para>
    /// Временный файл создаётся в каталоге цели — иначе переименование перестало бы быть
    /// атомарным (переезд между файловыми системами) и превратилось бы в копирование.
    /// Плата за это — потребность в праве записи на каталог: при записи в каталог, где менять
    /// можно только сам файл, скачивание теперь падает с <see cref="ErrorCode.NetworkError"/>
    /// вместо частичной перезаписи.
    /// </para>
    /// <para>
    /// Симлинк разыменовывается до записи, чтобы, как и прежде, обновлялся файл по ссылке,
    /// а не подменялась сама ссылка. Специальные приёмники (<c>/dev/null</c>, <c>/dev/tty</c>,
    /// именованные каналы под <c>/dev</c>, DOS-устройства вроде <c>NUL</c>) переименованием
    /// заменять нельзя — под root это уничтожило бы устройство, — поэтому в них пишем напрямую
    /// и ничего не удаляем; признаком неполноты, как и для <c>--out -</c>, служит exit-код.
    /// </para>
    /// </remarks>
    private static async Task<long> WriteToFile(
        Core.Api.TrackerDownload download,
        string target,
        bool force,
        TextWriter stderr,
        CancellationToken ct)
    {
        var writePath = ResolveWritePath(target);

        if (IsSpecialSink(writePath))
        {
            long written;
            await using (var sink = OpenForWrite(writePath, FileMode.Create, target))
            {
                written = await CopyCounting(download.Stream, sink, ct);
            }

            EnsureComplete(download, written);
            return written;
        }

        var tmp = writePath + ".part-" + Guid.NewGuid().ToString("N")[..8];
        var stream = OpenForWrite(tmp, FileMode.CreateNew, target);

        long bytes;
        try
        {
            await using (stream)
            {
                await download.Stream.CopyToAsync(stream, ct);
                bytes = stream.Length;
            }

            EnsureComplete(download, bytes);
            Commit(tmp, writePath, target, force);
        }
        catch
        {
            // Отмена (Ctrl-C, таймаут) или ошибка I/O посреди скачивания: удаляем заведомо
            // свой временный файл. Целевого файла к этому моменту либо нет, либо он не тронут.
            RemoveTemp(tmp, stderr);
            throw;
        }

        return bytes;
    }

    /// <summary>
    /// Открывает файл на запись, переводя локальные ошибки I/O в контракт ошибок CLI.
    /// </summary>
    /// <param name="path">Открываемый путь (временный файл или специальный приёмник).</param>
    /// <param name="mode">Режим открытия.</param>
    /// <param name="target">Целевой путь — для текста ошибки.</param>
    /// <returns>Открытый поток.</returns>
    /// <exception cref="TrackerException"><see cref="ErrorCode.NetworkError"/> — открыть не удалось.</exception>
    private static FileStream OpenForWrite(string path, FileMode mode, string target)
    {
        try
        {
            return new FileStream(path, mode, FileAccess.Write, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            throw new TrackerException(
                ErrorCode.NetworkError,
                $"could not open \"{path}\" for writing: {ex.Message} (target: {target})",
                inner: ex);
        }
    }

    /// <summary>
    /// Проверяет, что получено не меньше объявленного в <c>Content-Length</c>.
    /// </summary>
    /// <param name="download">Ответ с телом вложения.</param>
    /// <param name="written">Сколько байт записано.</param>
    /// <exception cref="TrackerException"><see cref="ErrorCode.NetworkError"/> — тело короче объявленного.</exception>
    private static void EnsureComplete(Core.Api.TrackerDownload download, long written)
    {
        if (download.ContentLength is { } expected && written < expected)
        {
            throw new TrackerException(
                ErrorCode.NetworkError,
                $"truncated download: expected {expected} bytes, got {written}");
        }
    }

    /// <summary>
    /// Переименовывает временный файл в целевой.
    /// </summary>
    /// <param name="tmp">Временный файл с полностью скачанным содержимым.</param>
    /// <param name="writePath">Путь записи (цель с разыменованными симлинками).</param>
    /// <param name="target">Целевой путь как его задал пользователь — для текста ошибки.</param>
    /// <param name="force">Разрешено ли перезаписывать.</param>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.InvalidArgs"/> — файл появился между проверкой и переименованием
    /// (гонка с другим процессом), <c>--force</c> не задан; <see cref="ErrorCode.NetworkError"/> —
    /// прочая ошибка переименования.
    /// </exception>
    private static void Commit(string tmp, string writePath, string target, bool force)
    {
        try
        {
            File.Move(tmp, writePath, overwrite: force);
        }
        catch (IOException ex) when (!force && File.Exists(writePath))
        {
            // Файла не было при проверке, но он появился до переименования. Молча затирать
            // чужой результат нельзя — это ровно тот случай, от которого защищает --force.
            throw new TrackerException(
                ErrorCode.InvalidArgs,
                $"file exists, use --force: {target}",
                inner: ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TrackerException(
                ErrorCode.NetworkError,
                $"could not finalize download into \"{target}\": {ex.Message}",
                inner: ex);
        }
    }

    /// <summary>
    /// Удаляет временный файл и сообщает об этом в stderr, чтобы удаление не выглядело
    /// как пропавший результат.
    /// </summary>
    /// <param name="tmp">Путь к временному файлу.</param>
    /// <param name="stderr">Writer для сообщения.</param>
    /// <remarks>
    /// Ошибку самого удаления подавляем: наверх должна уйти исходная причина обрыва,
    /// а не вторичный сбой уборки. В этом случае в stderr остаётся предупреждение о том,
    /// что временный файл остался на диске.
    /// </remarks>
    private static void RemoveTemp(string tmp, TextWriter stderr)
    {
        try
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
                stderr.WriteLine($"removed incomplete download: {tmp}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine(
                $"WARNING: incomplete download left on disk (could not remove it: {ex.Message}): {tmp}");
        }
    }

    /// <summary>
    /// Разыменовывает симлинк в целевом пути, чтобы запись обновляла файл по ссылке,
    /// а не подменяла саму ссылку.
    /// </summary>
    /// <param name="target">Целевой путь.</param>
    /// <returns>Путь, по которому следует писать.</returns>
    private static string ResolveWritePath(string target)
    {
        try
        {
            return File.ResolveLinkTarget(target, returnFinalTarget: true)?.FullName ?? target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Битая или зацикленная ссылка: пишем по исходному пути, как и раньше.
            return target;
        }
    }

    /// <summary>
    /// Определяет, что путь указывает на специальный приёмник, который нельзя подменять
    /// переименованием (устройство, канал, консоль).
    /// </summary>
    /// <param name="path">Проверяемый путь.</param>
    /// <returns><see langword="true"/> для устройств и псевдофайловых систем.</returns>
    /// <remarks>
    /// Портируемого способа спросить у .NET тип файла (st_mode) нет: <c>File.GetAttributes</c>
    /// на Unix различает только каталог и симлинк. Поэтому проверка идёт по расположению —
    /// всё под <c>/dev</c> и <c>/proc</c> и зарезервированные DOS-имена на Windows.
    /// Ошибиться в эту сторону безопасно: для такого пути мы просто ведём себя как раньше.
    /// </remarks>
    private static bool IsSpecialSink(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            if (path.StartsWith(@"\\.\", StringComparison.Ordinal)
                || path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return true;
            }

            var name = Path.GetFileNameWithoutExtension(path);
            return DosDevices.Contains(name);
        }

        var full = Path.GetFullPath(path);
        return full.StartsWith("/dev/", StringComparison.Ordinal)
            || full.StartsWith("/proc/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Зарезервированные имена устройств Windows, запись в которые не является записью в файл.
    /// </summary>
    private static readonly HashSet<string> DosDevices = new(StringComparer.OrdinalIgnoreCase)
    {
        "NUL", "CON", "AUX", "PRN",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Копирует поток, считая записанные байты: у специальных приёмников
    /// (<c>/dev/null</c> и прочих устройств) спрашивать <see cref="FileStream.Length"/> нельзя.
    /// </summary>
    /// <param name="source">Источник.</param>
    /// <param name="destination">Приёмник.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Количество записанных байт.</returns>
    private static async Task<long> CopyCounting(Stream source, Stream destination, CancellationToken ct)
    {
        const int chunkSize = 81920;
        var buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
        long total = 0;
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, chunkSize), ct)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                total += read;
            }

            await destination.FlushAsync(ct);
            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Копирует тело ответа в raw-stdout чанками, не буферизуя вложение целиком в память,
    /// и подсчитывает записанные байты.
    /// </summary>
    /// <remarks>
    /// Чтение из <paramref name="source"/> не защищено никаким catch: оборванное тело
    /// HTTP-ответа приходит как <see cref="System.Net.Http.HttpIOException"/>
    /// (наследник <see cref="IOException"/>) и должно стать ошибкой, а не тихим успехом.
    /// Подавляется только ошибка записи формы «оборванный пайп» — см. <see cref="IsBrokenPipe"/>.
    /// </remarks>
    /// <param name="source">Поток тела ответа.</param>
    /// <param name="stdout">Целевой поток стандартного вывода.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>
    /// Количество успешно записанных байт и признак того, что копирование прервано
    /// закрытым потребителем пайпа.
    /// </returns>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.NetworkError"/> — тело ответа оборвалось при чтении
    /// или запись в stdout упала по причине, не являющейся обрывом пайпа.
    /// </exception>
    private static async Task<(long Copied, bool PipeBroken)> CopyToStdout(
        Stream source,
        Stream stdout,
        CancellationToken ct)
    {
        const int chunkSize = 81920;
        var buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
        long total = 0;
        try
        {
            while (true)
            {
                int read;
                try
                {
                    // Пул может вернуть массив больше запрошенного — режем до chunkSize,
                    // чтобы размер чанка не зависел от внутренних бакетов ArrayPool.
                    read = await source.ReadAsync(buffer.AsMemory(0, chunkSize), ct);
                }
                catch (IOException ex)
                {
                    throw new TrackerException(
                        ErrorCode.NetworkError,
                        $"response stream failed after {total} bytes: {ex.Message}",
                        inner: ex);
                }

                if (read <= 0)
                {
                    break;
                }

                try
                {
                    await stdout.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                catch (IOException ex) when (IsBrokenPipe(ex))
                {
                    // Потребитель закрыл пайп (например `| head -c 100`) — штатный сценарий.
                    return (total, true);
                }
                catch (IOException ex)
                {
                    throw new TrackerException(
                        ErrorCode.NetworkError,
                        $"failed to write to stdout after {total} bytes: {ex.Message}",
                        inner: ex);
                }

                total += read;
            }

            try
            {
                await stdout.FlushAsync(ct);
            }
            catch (IOException ex) when (IsBrokenPipe(ex))
            {
                return (total, true);
            }
            catch (IOException ex)
            {
                throw new TrackerException(
                    ErrorCode.NetworkError,
                    $"failed to flush stdout after {total} bytes: {ex.Message}",
                    inner: ex);
            }

            return (total, false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Определяет, что ошибка записи — именно закрытый потребителем пайп,
    /// а не любая другая I/O-проблема (например ENOSPC).
    /// </summary>
    /// <param name="ex">Исключение, полученное при записи в stdout.</param>
    /// <returns><see langword="true"/> для EPIPE / ERROR_BROKEN_PIPE / ERROR_NO_DATA.</returns>
    private static bool IsBrokenPipe(IOException ex)
    {
        if (OperatingSystem.IsWindows())
        {
            const int errorBrokenPipe = 109;
            const int errorNoData = 232;
            return ex.HResult == errorBrokenPipe
                || ex.HResult == errorNoData
                || ex.HResult == unchecked((int)0x8007006D)
                || ex.HResult == unchecked((int)0x800700E8);
        }

        // На Unix .NET кладёт в HResult сырой errno; EPIPE == 32.
        const int epipe = 32;
        return ex.HResult == epipe;
    }
}
