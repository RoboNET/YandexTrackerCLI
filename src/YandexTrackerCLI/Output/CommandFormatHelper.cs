namespace YandexTrackerCLI.Output;

using System.CommandLine;
using Commands;

/// <summary>
/// Хелпер для резолва эффективного формата вывода в командах CLI.
/// </summary>
/// <remarks>
/// Команды, которые проходят через <c>TrackerContextFactory.CreateAsync</c>, могут
/// использовать <c>ctx.EffectiveOutputFormat</c>. Команды, которые работают только с
/// конфигом (например, <c>config get/list/set</c>, <c>auth login/logout</c>), вызывают
/// <see cref="ResolveForCommand"/> напрямую.
/// </remarks>
public static class CommandFormatHelper
{
    /// <summary>
    /// Резолвит формат на основе CLI флага <c>--format</c>, env <c>YT_FORMAT</c>,
    /// <paramref name="profileDefaultFormat"/> и TTY-detection.
    /// </summary>
    /// <param name="parseResult">Результат парсинга CLI.</param>
    /// <param name="profileDefaultFormat">Значение <c>default_format</c> из профиля или <c>null</c>.</param>
    /// <returns>Резолвленный формат (никогда <see cref="OutputFormat.Auto"/>).</returns>
    public static OutputFormat ResolveForCommand(
        ParseResult parseResult,
        string? profileDefaultFormat = null)
    {
        var cliFormat = parseResult.GetValue(RootCommandBuilder.FormatOption);
        var env = EnvReader.Snapshot();
        return FormatResolver.Resolve(
            cliFormat,
            env,
            profileDefaultFormat,
            isOutputRedirected: Console.IsOutputRedirected);
    }

    /// <summary>
    /// Возвращает признак pretty-печати для JSON: <c>false</c>, если stdout перенаправлен;
    /// <c>true</c> — если идёт в TTY.
    /// </summary>
    public static bool ResolvePretty() => !Console.IsOutputRedirected;
}
