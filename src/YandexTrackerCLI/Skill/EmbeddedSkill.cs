namespace YandexTrackerCLI.Skill;

using System.Reflection;
using System.Text.RegularExpressions;

/// <summary>
/// AOT-safe доступ к embedded SKILL.md (LogicalName <c>yt.skill.md</c>) и его метаданным.
/// Версия из <see cref="AssemblyInformationalVersionAttribute"/> подставляется в плейсхолдер
/// <c>{VERSION}</c> внутри тела skill'а.
/// </summary>
public static class EmbeddedSkill
{
    /// <summary>
    /// Имя embedded resource, как задано в <c>YandexTrackerCLI.csproj</c>.
    /// </summary>
    public const string ResourceName = "yt.skill.md";

    /// <summary>
    /// Fallback-описание skill'а, если в embedded SKILL.md по какой-то причине нет
    /// frontmatter-поля <c>description</c>.
    /// </summary>
    public const string DefaultDescription = "Yandex Tracker CLI skill";

    /// <summary>
    /// Шаблон для маркера версии SKILL.md (<c>&lt;!-- yt-version: X.Y.Z --&gt;</c>),
    /// который используется для определения версии установленного skill'а
    /// (универсально для всех target'ов — у всех вариантов файла этот маркер присутствует).
    /// </summary>
    private static readonly Regex VersionMarkerRegex =
        new(@"<!--\s*yt-version:\s*([^\s>]+)\s*-->", RegexOptions.Compiled);

    /// <summary>
    /// Возвращает полный SKILL.md (с frontmatter) с подставленной актуальной версией
    /// в плейсхолдер <c>{VERSION}</c>.
    /// </summary>
    /// <returns>Содержимое файла SKILL.md с резолвленной версией.</returns>
    /// <exception cref="InvalidOperationException">
    /// Если embedded resource <see cref="ResourceName"/> не найден в сборке (program bug).
    /// </exception>
    public static string ReadAll()
    {
        var asm = typeof(EmbeddedSkill).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found in assembly '{asm.GetName().Name}'.");
        using var reader = new StreamReader(stream);
        var raw = reader.ReadToEnd();
        return raw.Replace("{VERSION}", GetVersion(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Возвращает SKILL.md без YAML frontmatter (между первой и второй парой <c>---</c>).
    /// Если frontmatter отсутствует — возвращает полный текст. Сохранено как утилита
    /// для команд <c>yt skill show</c> (на случай, если кому-то нужно тело без YAML).
    /// </summary>
    /// <returns>Тело skill'а без YAML frontmatter.</returns>
    public static string ReadBodyOnly()
    {
        var full = ReadAll();
        return StripFrontmatter(full);
    }

    /// <summary>
    /// Возвращает версию текущей сборки (<see cref="AssemblyInformationalVersionAttribute"/>),
    /// очищенную от <c>+&lt;build-metadata&gt;</c>. Если атрибут отсутствует — <c>"unknown"</c>.
    /// </summary>
    /// <returns>Строковое представление версии (например, <c>"0.1.2"</c> или <c>"0.2.0-preview.1"</c>).</returns>
    public static string GetVersion()
    {
        var attr = typeof(EmbeddedSkill).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var version = attr?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            return "unknown";
        }

        // Strip +sourceRevisionId (MSBuild добавляет +<commit-hash> к InformationalVersion).
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }

    /// <summary>
    /// Извлекает значение версии из строки, содержащей маркер
    /// <c>&lt;!-- yt-version: X.Y.Z --&gt;</c>. Возвращает <c>null</c> если маркер не найден
    /// или содержит unresolved плейсхолдер <c>{VERSION}</c> (это бывает при probe
    /// исходника репо, который сам содержит шаблон).
    /// </summary>
    /// <param name="text">Текст для парсинга (обычно содержимое установленного SKILL.md).</param>
    /// <returns>Версия из маркера или <c>null</c>.</returns>
    public static string? TryExtractClaudeVersion(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var m = VersionMarkerRegex.Match(text);
        if (!m.Success)
        {
            return null;
        }
        var v = m.Groups[1].Value;
        // Unresolved placeholder — probe рабочей копии репозитория, не реальной установки.
        return v == "{VERSION}" ? null : v;
    }

    /// <summary>
    /// Возвращает значение поля <c>description</c> из YAML frontmatter embedded SKILL.md.
    /// Если frontmatter отсутствует или поле не найдено — возвращает <see cref="DefaultDescription"/>.
    /// Используется для генерации Cursor <c>.mdc</c> и Copilot <c>.instructions.md</c>.
    /// </summary>
    /// <returns>Описание (одна строка).</returns>
    public static string GetDescription()
    {
        var content = ReadAll();
        var fm = ParseFrontmatter(content);
        return fm.TryGetValue("description", out var desc) && !string.IsNullOrWhiteSpace(desc)
            ? desc
            : DefaultDescription;
    }

    /// <summary>
    /// Возвращает фрагмент embedded SKILL.md, идущий после маркера <c>&lt;!-- yt-version: ... --&gt;</c>,
    /// с очищенными ведущими пустыми строками. Используется для перепаковки контента
    /// под Cursor (<c>.mdc</c>) и Copilot (<c>.instructions.md</c>) с другим frontmatter.
    /// </summary>
    /// <returns>Тело skill'а после version-маркера.</returns>
    public static string GetBodyAfterVersionMarker()
    {
        var content = ReadAll();
        var m = VersionMarkerRegex.Match(content);
        if (!m.Success)
        {
            // Маркера нет — fallback на body после frontmatter.
            return StripFrontmatter(content);
        }

        var afterMarker = m.Index + m.Length;
        // Сдвигаем за перевод строки маркера, если он есть.
        if (afterMarker < content.Length && content[afterMarker] == '\r')
        {
            afterMarker++;
        }
        if (afterMarker < content.Length && content[afterMarker] == '\n')
        {
            afterMarker++;
        }
        // Убираем ведущие пустые строки.
        while (afterMarker < content.Length && (content[afterMarker] == '\n' || content[afterMarker] == '\r'))
        {
            afterMarker++;
        }
        return content[afterMarker..];
    }

    /// <summary>
    /// Парсит YAML frontmatter (между первой и второй строкой <c>---</c>) в простой
    /// словарь <c>key: value</c>. Поддерживает только однострочные значения; сложный YAML
    /// (вложенные структуры, multiline strings, lists) не нужен — frontmatter SKILL.md
    /// тривиален: <c>name</c>, <c>description</c>.
    /// </summary>
    /// <param name="content">Полное содержимое markdown-файла.</param>
    /// <returns>Словарь полей frontmatter; пустой, если frontmatter отсутствует.</returns>
    private static Dictionary<string, string> ParseFrontmatter(string content)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!content.StartsWith("---", StringComparison.Ordinal))
        {
            return dict;
        }

        var afterFirst = content.IndexOf('\n');
        if (afterFirst < 0)
        {
            return dict;
        }

        var searchFrom = afterFirst + 1;
        while (searchFrom < content.Length)
        {
            var nextNl = content.IndexOf('\n', searchFrom);
            var line = nextNl < 0 ? content[searchFrom..] : content[searchFrom..nextNl];
            var trimmed = line.TrimEnd('\r');
            if (trimmed == "---")
            {
                return dict;
            }

            var colon = trimmed.IndexOf(':');
            if (colon > 0)
            {
                var key = trimmed[..colon].Trim();
                var value = trimmed[(colon + 1)..].Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    dict[key] = value;
                }
            }

            if (nextNl < 0)
            {
                break;
            }
            searchFrom = nextNl + 1;
        }
        return dict;
    }

    /// <summary>
    /// Удаляет YAML frontmatter (между первой и второй строкой <c>---</c>) из текста.
    /// Если frontmatter отсутствует — возвращает входной текст без изменений. Также
    /// подрезает пустые строки, оставшиеся в начале.
    /// </summary>
    private static string StripFrontmatter(string text)
    {
        // Frontmatter обязан начинаться на первой строке.
        if (!text.StartsWith("---", StringComparison.Ordinal))
        {
            return text;
        }

        // Найдём конец первой строки разделителя.
        var afterFirst = text.IndexOf('\n');
        if (afterFirst < 0)
        {
            return text;
        }

        // Ищем закрывающий разделитель `---` на отдельной строке.
        var searchFrom = afterFirst + 1;
        while (searchFrom < text.Length)
        {
            var nextNl = text.IndexOf('\n', searchFrom);
            var line = nextNl < 0
                ? text[searchFrom..]
                : text[searchFrom..nextNl];
            // Trim trailing CR.
            var trimmed = line.TrimEnd('\r');
            if (trimmed == "---")
            {
                var bodyStart = nextNl < 0 ? text.Length : nextNl + 1;
                // Убираем ведущие пустые строки.
                while (bodyStart < text.Length && (text[bodyStart] == '\n' || text[bodyStart] == '\r'))
                {
                    bodyStart++;
                }
                return text[bodyStart..];
            }
            if (nextNl < 0)
            {
                break;
            }
            searchFrom = nextNl + 1;
        }

        // Закрывающего разделителя не нашли — возвращаем как есть.
        return text;
    }
}
