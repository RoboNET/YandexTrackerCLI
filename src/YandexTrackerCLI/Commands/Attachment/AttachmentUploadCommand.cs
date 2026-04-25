namespace YandexTrackerCLI.Commands.Attachment;

using System.CommandLine;
using System.Net.Http.Headers;
using Core.Api.Errors;
using Output;

/// <summary>
/// Команда <c>yt attachment upload &lt;issue-key&gt; &lt;file-path&gt; [--name &lt;override&gt;]</c>:
/// загружает файл во вложения задачи через <c>POST /v3/issues/{key}/attachments</c>
/// в формате <c>multipart/form-data</c>. Тело файла стримится из <see cref="FileStream"/>.
/// </summary>
/// <remarks>
/// Поле multipart-формы называется <c>file</c>. Имя файла в Tracker'е определяется так:
/// значение опции <c>--name</c>, если задано; иначе — <see cref="Path.GetFileName(string)"/>
/// от <paramref name="filePath"/>. Если путь не существует — <see cref="ErrorCode.InvalidArgs"/>
/// (exit 2).
/// </remarks>
public static class AttachmentUploadCommand
{
    /// <summary>
    /// Строит subcommand <c>upload</c> для <c>yt attachment</c>.
    /// </summary>
    /// <returns>Сконфигурированная <see cref="Command"/>.</returns>
    public static Command Build()
    {
        var keyArg = new Argument<string>("issue-key") { Description = "Ключ задачи (например DEV-1)." };
        var fileArg = new Argument<string>("file-path") { Description = "Путь к файлу для загрузки." };
        var nameOpt = new Option<string?>("--name")
        {
            Description = "Имя файла в Tracker'е (по умолчанию — имя файла на диске).",
        };

        var cmd = new Command("upload", "Загрузить файл во вложения задачи (POST multipart/form-data).");
        cmd.Arguments.Add(keyArg);
        cmd.Arguments.Add(fileArg);
        cmd.Options.Add(nameOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            try
            {
                var key = pr.GetValue(keyArg)!;
                var filePath = pr.GetValue(fileArg)!;
                var overrideName = pr.GetValue(nameOpt);

                if (!File.Exists(filePath))
                {
                    throw new TrackerException(ErrorCode.InvalidArgs, $"file not found: {filePath}");
                }

                var uploadName = string.IsNullOrWhiteSpace(overrideName)
                    ? Path.GetFileName(filePath)
                    : overrideName;

                using var ctx = await TrackerContextFactory.CreateAsync(
                    profileName: pr.GetValue(RootCommandBuilder.ProfileOption),
                    cliReadOnly: pr.GetValue(RootCommandBuilder.ReadOnlyOption),
                    timeoutSeconds: pr.GetValue(RootCommandBuilder.TimeoutOption),
                    wireLogPath: pr.GetValue(RootCommandBuilder.LogFileOption),
                    wireLogMask: !pr.GetValue(RootCommandBuilder.LogRawOption),
                    cliFormat: pr.GetValue(RootCommandBuilder.FormatOption),
                    ct: ct);

                await using var file = File.OpenRead(filePath);
                using var multipart = new MultipartFormDataContent();
                var streamContent = new StreamContent(file);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                multipart.Add(streamContent, name: "file", fileName: uploadName);

                var result = await ctx.Client.PostMultipartAsync(
                    $"issues/{Uri.EscapeDataString(key)}/attachments",
                    multipart,
                    ct);

                JsonWriter.Write(Console.Out, result, ctx.EffectiveOutputFormat, pretty: !Console.IsOutputRedirected);
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
