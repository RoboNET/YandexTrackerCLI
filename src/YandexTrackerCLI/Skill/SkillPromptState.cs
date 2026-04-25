namespace YandexTrackerCLI.Skill;

using System.Runtime.InteropServices;
using System.Text.Json;

/// <summary>
/// Persistent state для auto-check механизма: запоминает выбор пользователя
/// (decline для версии / never prompt) и какие версии уже выводили warning'и в pipe.
/// </summary>
public sealed class SkillPromptState
{
    /// <summary>
    /// Последняя бинарная версия, с которой пользователь работал. Позволяет диагностировать.
    /// </summary>
    public string? LastSeenBinaryVersion { get; set; }

    /// <summary>
    /// Версии бинаря, для которых пользователь сказал «нет» в интерактивном prompt.
    /// </summary>
    public List<string> DeclinedForVersions { get; set; } = new();

    /// <summary>
    /// <c>true</c>, если пользователь выбрал <c>never</c> — больше не спрашивать никогда.
    /// </summary>
    public bool NeverPrompt { get; set; }

    /// <summary>
    /// Версии бинаря, для которых уже выводили non-TTY warning. Чтобы не спамить
    /// stderr на каждом вызове из pipe.
    /// </summary>
    public List<string> LastWarnedForVersions { get; set; } = new();

    /// <summary>
    /// Загружает state из <see cref="SkillPaths.PromptStateFile"/>. Если файла нет
    /// или он повреждён — возвращает свежий пустой state.
    /// </summary>
    public static SkillPromptState Load()
    {
        var path = SkillPaths.PromptStateFile();
        if (!File.Exists(path))
        {
            return new SkillPromptState();
        }
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var state = new SkillPromptState();
            if (root.TryGetProperty("last_seen_binary_version", out var lsv) && lsv.ValueKind == JsonValueKind.String)
            {
                state.LastSeenBinaryVersion = lsv.GetString();
            }
            if (root.TryGetProperty("never_prompt", out var np) && (np.ValueKind == JsonValueKind.True || np.ValueKind == JsonValueKind.False))
            {
                state.NeverPrompt = np.GetBoolean();
            }
            if (root.TryGetProperty("declined_for_versions", out var dv) && dv.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in dv.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String && el.GetString() is { } s)
                    {
                        state.DeclinedForVersions.Add(s);
                    }
                }
            }
            if (root.TryGetProperty("last_warned_for_versions", out var lw) && lw.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in lw.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String && el.GetString() is { } s)
                    {
                        state.LastWarnedForVersions.Add(s);
                    }
                }
            }
            return state;
        }
        catch
        {
            return new SkillPromptState();
        }
    }

    /// <summary>
    /// Сохраняет state в <see cref="SkillPaths.PromptStateFile"/>, создавая
    /// родительский каталог при необходимости. На POSIX выставляет <c>0600</c>.
    /// </summary>
    public void Save()
    {
        var path = SkillPaths.PromptStateFile();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            if (LastSeenBinaryVersion is { } lsv)
            {
                w.WriteString("last_seen_binary_version", lsv);
            }
            w.WriteBoolean("never_prompt", NeverPrompt);
            w.WriteStartArray("declined_for_versions");
            foreach (var v in DeclinedForVersions.Distinct(StringComparer.Ordinal))
            {
                w.WriteStringValue(v);
            }
            w.WriteEndArray();
            w.WriteStartArray("last_warned_for_versions");
            foreach (var v in LastWarnedForVersions.Distinct(StringComparer.Ordinal))
            {
                w.WriteStringValue(v);
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        File.WriteAllBytes(path, ms.ToArray());

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                File.SetUnixFileMode(path, (UnixFileMode)0b110_000_000); // 0600
            }
            catch
            {
                // best-effort
            }
        }
    }

    /// <summary>
    /// Полностью удаляет state-файл. Используется командой <c>yt skill check --reset-prompt-state</c>.
    /// </summary>
    public static bool Reset()
    {
        var path = SkillPaths.PromptStateFile();
        if (!File.Exists(path))
        {
            return false;
        }
        File.Delete(path);
        return true;
    }
}
