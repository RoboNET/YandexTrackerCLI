namespace YandexTrackerCLI.Tests.Plugin;

using System.Text.Json;
using System.Text.RegularExpressions;
using TUnit.Core;

/// <summary>
/// Тесты согласованности плагинных манифестов репозитория: <c>.claude-plugin/plugin.json</c>
/// (версия проставляется релизным workflow <c>prepare-release</c> вместе с маркером
/// <c>yt-version</c> в SKILL.md) и
/// <c>.claude-plugin/marketplace.json</c> (источник плагинов для <c>claude plugin marketplace add</c>).
/// </summary>
/// <remarks>
/// Тесты работают только когда доступно рабочее дерево репозитория. Если его нет
/// (тесты гоняют из пакета), они падают, а не молча проходят: no-op-тест не отличим
/// в отчёте от настоящей проверки и создаёт ложное ощущение покрытия.
/// </remarks>
public sealed class PluginManifestTests
{
    [Test]
    public async Task Marketplace_ReferencesPluginAndDoesNotDuplicateVersion()
    {
        var repoRoot = RequireRepoRoot();

        var pluginPath = Path.Combine(repoRoot, ".claude-plugin", "plugin.json");
        var marketplacePath = Path.Combine(repoRoot, ".claude-plugin", "marketplace.json");
        await Assert.That(File.Exists(pluginPath)).IsTrue();
        await Assert.That(File.Exists(marketplacePath)).IsTrue();

        using var plugin = JsonDocument.Parse(File.ReadAllText(pluginPath));
        using var marketplace = JsonDocument.Parse(File.ReadAllText(marketplacePath));

        var pluginName = plugin.RootElement.GetProperty("name").GetString();
        var pluginVersion = plugin.RootElement.GetProperty("version").GetString();
        await Assert.That(string.IsNullOrWhiteSpace(pluginName)).IsFalse();
        await Assert.That(string.IsNullOrWhiteSpace(pluginVersion)).IsFalse();

        var entries = marketplace.RootElement.GetProperty("plugins").EnumerateArray().ToList();
        await Assert.That(entries.Count).IsEqualTo(1);
        var entry = entries[0];

        // Запись маркетплейса должна ссылаться на тот же плагин и на его каталог в этом же репо.
        await Assert.That(entry.GetProperty("name").GetString()).IsEqualTo(pluginName);

        // `source` допускает две валидные формы: строку-путь и объект ({source, repo}).
        // Разбираем обе явно, иначе GetString() на объектной форме бросил бы
        // InvalidOperationException вместо внятного assert'а.
        var source = entry.GetProperty("source");
        await Assert.That(source.ValueKind).IsEqualTo(JsonValueKind.String);
        await Assert.That(source.GetString()).IsEqualTo("./");

        // Версия намеренно не дублируется: авторитет — plugin.json, иначе её пришлось бы
        // проставлять в двух местах и они молча разъехались бы (plugin.json перебивает
        // значение из entry без предупреждения).
        await Assert.That(entry.TryGetProperty("version", out _)).IsFalse();

        // Каноничная форма описания маркетплейса — top-level `description`;
        // `metadata.description` принимается только ради обратной совместимости.
        await Assert.That(marketplace.RootElement.TryGetProperty("description", out var desc)).IsTrue();
        await Assert.That(string.IsNullOrWhiteSpace(desc.GetString())).IsFalse();
    }

    [Test]
    public async Task Plugin_PointsAtSkillsDirectoryThatExists()
    {
        var repoRoot = RequireRepoRoot();

        using var plugin = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repoRoot, ".claude-plugin", "plugin.json")));
        var skills = plugin.RootElement.GetProperty("skills");
        await Assert.That(skills.ValueKind).IsEqualTo(JsonValueKind.String);
        var skillsRelative = skills.GetString()!;
        await Assert.That(skillsRelative.StartsWith("./", StringComparison.Ordinal)).IsTrue();

        var skillsDir = Path.Combine(repoRoot, skillsRelative[2..].Replace('/', Path.DirectorySeparatorChar));
        await Assert.That(Directory.Exists(skillsDir)).IsTrue();
        await Assert.That(File.Exists(Path.Combine(skillsDir, "yt", "SKILL.md"))).IsTrue();
    }

    [Test]
    public async Task PluginVersion_AndSkillMarker_AreConsistent()
    {
        // SKILL.md — доставляемый артефакт: маркетплейс и универсальные установщики читают его
        // из репозитория, бинарь в доставке больше не участвует и версию нигде не подставляет.
        // Значит маркер обязан нести реальную версию. Ловится этим ровно ручная правка: кто-то
        // поднял версию в одном файле из двух. Полустемпленное состояние от упавшего
        // prepare-release сюда доехать не может — обе правки делает один шаг, коммит следующий.
        var repoRoot = RequireRepoRoot();

        using var plugin = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repoRoot, ".claude-plugin", "plugin.json")));
        var pluginVersion = plugin.RootElement.GetProperty("version").GetString();

        var skillText = File.ReadAllText(Path.Combine(repoRoot, ".claude", "skills", "yt", "SKILL.md"));
        var marker = VersionMarkerRegex.Match(skillText);
        await Assert.That(marker.Success).IsTrue();

        var markerVersion = marker.Groups[1].Value;

        // Одного равенства мало: если оба файла понесут одинаковую чушь ({VERSION} в обоих,
        // "dev", пустая строка), сравнение останется зелёным, а потребитель маркетплейса
        // получит литерал. Поэтому отдельно требуем semver-форму.
        await Assert.That(SemVerRegex.IsMatch(pluginVersion!)).IsTrue();
        await Assert.That(markerVersion).IsEqualTo(pluginVersion);
    }

    /// <summary>
    /// Строгий semver (core + optional pre-release, без build-metadata) — тот же шаблон,
    /// которым <c>prepare-release</c> валидирует входную версию. Ведущие нули запрещены:
    /// MinVer проигнорировал бы такой тег, и бинарь разъехался бы с манифестами.
    /// </summary>
    private static readonly Regex SemVerRegex = new(
        @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"
        + @"(-(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(\.(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Маркер версии в SKILL.md: <c>&lt;!-- yt-version: X.Y.Z --&gt;</c>. Тот же шаблон
    /// использует шаг простановки в <c>prepare-release.yml</c>.
    /// </summary>
    private static readonly Regex VersionMarkerRegex =
        new(@"<!--\s*yt-version:\s*([^\s>]+)\s*-->", RegexOptions.Compiled);

    /// <summary>
    /// Поднимается от каталога сборки к корню репозитория (по наличию <c>.claude-plugin/plugin.json</c>).
    /// </summary>
    /// <returns>Путь до корня репо.</returns>
    /// <exception cref="InvalidOperationException">Если рабочее дерево репозитория недоступно.</exception>
    private static string RequireRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, ".claude-plugin", "plugin.json")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException(
            $"Repository working tree not found above '{AppContext.BaseDirectory}'; "
            + "these tests validate repository manifests and cannot run without it.");
    }
}
