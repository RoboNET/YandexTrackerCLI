namespace YandexTrackerCLI.Commands.Skill;

using System.Text.Json;
using YandexTrackerCLI.Skill;

/// <summary>
/// Чистые форматтеры JSON-ответов для команд группы <c>yt skill</c>.
/// Возвращают готовый <see cref="JsonDocument"/>, который потом печатается через
/// <see cref="Output.JsonWriter.Write"/> с учётом <c>--format</c>.
/// </summary>
internal static class SkillJsonFormatter
{
    public static JsonDocument FormatInstall(IEnumerable<SkillManager.InstallResult> installed)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteStartArray("installed");
            foreach (var i in installed)
            {
                WriteInstallResult(w, i);
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return JsonDocument.Parse(ms.ToArray());
    }

    public static JsonDocument FormatUninstall(
        IEnumerable<(SkillTarget Target, SkillScope Scope, string Path)> uninstalled,
        IEnumerable<(SkillTarget Target, SkillScope Scope, string Path)> skipped)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteStartArray("uninstalled");
            foreach (var u in uninstalled)
            {
                w.WriteStartObject();
                w.WriteString("target", u.Target.ToString().ToLowerInvariant());
                w.WriteString("scope", u.Scope.ToString().ToLowerInvariant());
                w.WriteString("path", u.Path);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("skipped");
            foreach (var s in skipped)
            {
                w.WriteStartObject();
                w.WriteString("target", s.Target.ToString().ToLowerInvariant());
                w.WriteString("scope", s.Scope.ToString().ToLowerInvariant());
                w.WriteString("path", s.Path);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return JsonDocument.Parse(ms.ToArray());
    }

    public static JsonDocument FormatStatus(SkillManager.Status status, SkillPromptState state)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("current_version", status.CurrentVersion);

            w.WriteStartObject("claude");
            WriteSlot(w, "global", status.ClaudeGlobal, status.CurrentVersion);
            WriteSlot(w, "project", status.ClaudeProject, status.CurrentVersion);
            w.WriteEndObject();

            w.WriteStartObject("codex");
            WriteSlot(w, "global", status.CodexGlobal, status.CurrentVersion);
            WriteSlot(w, "project", status.CodexProject, status.CurrentVersion);
            w.WriteEndObject();

            w.WriteBoolean("any_outdated", status.AnyOutdated);

            w.WriteStartObject("prompt_state");
            w.WriteBoolean("never_prompt", state.NeverPrompt);
            w.WriteStartArray("declined_for_versions");
            foreach (var v in state.DeclinedForVersions.Distinct(StringComparer.Ordinal))
            {
                w.WriteStringValue(v);
            }
            w.WriteEndArray();
            w.WriteEndObject();

            w.WriteEndObject();
        }
        return JsonDocument.Parse(ms.ToArray());
    }

    public static JsonDocument FormatUpdate(
        IReadOnlyList<SkillManager.InstallResult> updated,
        bool hadInstallations)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteStartArray("updated");
            foreach (var u in updated)
            {
                w.WriteStartObject();
                w.WriteString("target", u.Target.ToString().ToLowerInvariant());
                w.WriteString("scope", u.Scope.ToString().ToLowerInvariant());
                w.WriteString("path", u.Path);
                w.WriteString("from_version", u.FromVersion ?? "unknown");
                w.WriteString("to_version", u.Version);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            if (!hadInstallations)
            {
                w.WriteString("message", "No installations found. Run `yt skill install` first.");
            }
            w.WriteEndObject();
        }
        return JsonDocument.Parse(ms.ToArray());
    }

    private static void WriteInstallResult(Utf8JsonWriter w, SkillManager.InstallResult i)
    {
        w.WriteStartObject();
        w.WriteString("target", i.Target.ToString().ToLowerInvariant());
        w.WriteString("scope", i.Scope.ToString().ToLowerInvariant());
        w.WriteString("path", i.Path);
        w.WriteString("version", i.Version);
        w.WriteEndObject();
    }

    private static void WriteSlot(Utf8JsonWriter w, string name, SkillManager.Installation? inst, string current)
    {
        if (inst is null)
        {
            w.WriteNull(name);
            return;
        }
        w.WriteStartObject(name);
        w.WriteString("path", inst.Path);
        w.WriteString("installed_version", inst.Version);
        w.WriteBoolean("up_to_date", string.Equals(inst.Version, current, StringComparison.Ordinal));
        w.WriteEndObject();
    }
}
