namespace YandexTrackerCLI.Tests.Commands.Skill;

using System.Security.Cryptography;
using System.Text;
using TUnit.Core;
using YandexTrackerCLI.Skill;

/// <summary>
/// Тесты <see cref="EmbeddedSkill"/>: подстановка версии, отсутствие frontmatter в body,
/// соответствие embedded SKILL.md рабочей копии в репо (с подстановкой <c>{VERSION}</c>).
/// </summary>
public sealed class EmbeddedSkillTests
{
    [Test]
    public async Task ReadAll_SubstitutesVersion()
    {
        var content = EmbeddedSkill.ReadAll();
        await Assert.That(content).Contains($"<!-- yt-version: {EmbeddedSkill.GetVersion()} -->");
        await Assert.That(content).DoesNotContain("{VERSION}");
    }

    [Test]
    public async Task ReadAll_HasLfLineEndings()
    {
        // SKILL.md может оказаться с CRLF на Windows checkout (git autocrlf=true);
        // ReadAll должен привести к LF, иначе тесты, сравнивающие на "---\n", упадут.
        var content = EmbeddedSkill.ReadAll();
        await Assert.That(content.Contains('\r')).IsFalse();
    }

    [Test]
    public async Task ReadBodyOnly_StripsFrontmatter()
    {
        var body = EmbeddedSkill.ReadBodyOnly();
        await Assert.That(body).DoesNotContain("name: yt");
        await Assert.That(body).Contains("# yt — Yandex Tracker CLI");
    }

    [Test]
    public async Task GetVersion_NotEmpty()
    {
        var v = EmbeddedSkill.GetVersion();
        await Assert.That(string.IsNullOrWhiteSpace(v)).IsFalse();
    }

    [Test]
    public async Task GetDescription_NotEmptyAndNotDefault()
    {
        // SKILL.md содержит реальный description в YAML frontmatter — должен быть извлечён.
        var desc = EmbeddedSkill.GetDescription();
        await Assert.That(string.IsNullOrWhiteSpace(desc)).IsFalse();
        await Assert.That(desc).IsNotEqualTo(EmbeddedSkill.DefaultDescription);
        // Description содержит ключевые маркеры из реального SKILL.md.
        await Assert.That(desc).Contains("Tracker");
    }

    [Test]
    public async Task GetBodyAfterVersionMarker_StripsMarkerAndFrontmatter()
    {
        var body = EmbeddedSkill.GetBodyAfterVersionMarker();
        // В body не должно быть ни frontmatter, ни самого маркера.
        await Assert.That(body).DoesNotContain("name: yt");
        await Assert.That(body).DoesNotContain("<!-- yt-version:");
        // Тело начинается с заголовка skill'а.
        await Assert.That(body.TrimStart()).StartsWith("# yt — Yandex Tracker CLI");
    }

    [Test]
    public async Task EmbeddedResource_MatchesWorkingCopy()
    {
        // Найдём рабочую копию SKILL.md, поднимаясь от каталога с тестами к корню репо.
        var probe = AppContext.BaseDirectory;
        string? repoRoot = null;
        for (var dir = new DirectoryInfo(probe); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".claude", "skills", "yt")))
            {
                repoRoot = dir.FullName;
                break;
            }
        }
        if (repoRoot is null)
        {
            // Не нашли working tree (пакет/CI без репо) — пропускаем.
            return;
        }

        var workingCopy = File.ReadAllText(Path.Combine(repoRoot, ".claude", "skills", "yt", "SKILL.md"));
        // Нормализуем CRLF→LF: на Windows git с autocrlf=true может закоммитить файл с CRLF;
        // EmbeddedSkill.ReadAll также нормализует, поэтому сравнение должно идти на одинаковом базисе.
        var workingCopyLf = workingCopy.Replace("\r\n", "\n").Replace("\r", "\n");
        var withVersion = workingCopyLf.Replace("{VERSION}", EmbeddedSkill.GetVersion());
        var embedded = EmbeddedSkill.ReadAll();

        var sha1 = Sha256(withVersion);
        var sha2 = Sha256(embedded);
        await Assert.That(sha1).IsEqualTo(sha2);
    }

    private static string Sha256(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
