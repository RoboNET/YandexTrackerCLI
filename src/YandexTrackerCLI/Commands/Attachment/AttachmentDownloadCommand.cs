namespace YandexTrackerCLI.Commands.Attachment;

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt attachment download &lt;issue-key&gt; &lt;attachment-id&gt;
/// [--out &lt;path&gt;] [--force]</c>: скачивает вложение через
/// <c>GET /v3/issues/{key}/attachments/{id}/download</c> со стримингом тела ответа
/// в файл.
/// </summary>
/// <remarks>
/// <para>
/// Целевой путь определяется так:
/// <list type="number">
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
/// </remarks>
public static class AttachmentDownloadCommand
{
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
            Description = "Явный путь для сохранения файла.",
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
}
