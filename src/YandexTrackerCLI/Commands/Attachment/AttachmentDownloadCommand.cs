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
                        Console.Error.WriteLine(
                            $"downloaded {copied} bytes (truncated: consumer closed the pipe)");
                        return 0;
                    }

                    if (download.ContentLength is { } expected && copied < expected)
                    {
                        throw new TrackerException(
                            ErrorCode.NetworkError,
                            $"truncated download: expected {expected} bytes, got {copied}");
                    }

                    Console.Error.WriteLine($"downloaded {copied} bytes");
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

                var mode = force ? FileMode.Create : FileMode.CreateNew;
                long bytes;
                await using (var fs = new FileStream(target, mode, FileAccess.Write, FileShare.None))
                {
                    await download.Stream.CopyToAsync(fs, ct);
                    bytes = fs.Length;
                }

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
                ErrorWriter.Write(Console.Error, ex);
                return ex.Code.ToExitCode();
            }
        });
        return cmd;
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
