namespace YandexTrackerCLI.Core.Tests.Config;

using TUnit.Core;
using YandexTrackerCLI.Core.Config;
using YandexTrackerCLI.Core.Http;

/// <summary>
/// Тесты позиционного поиска ресурса автоматизаций и percent-декодирования сегментов —
/// того уровня, на котором декодирование действительно происходит. В guard'е такой кейс
/// не проверить: <see cref="Uri"/> канонизирует unreserved-символы ещё при склейке
/// относительного пути с BaseAddress, и до сравнения доходит уже готовое слово.
/// </summary>
public sealed class ExternalEffectsPolicySegmentTests
{
    private static string[] Decoded(string path) =>
        Array.ConvertAll(RequestUriPath.Segments(path), RequestUriPath.Decode);

    /// <summary>
    /// Ресурс автоматизаций на своей позиции — найден, и найдено каноническое имя.
    /// </summary>
    /// <param name="path">Путь запроса.</param>
    /// <param name="expected">Ожидаемое каноническое имя сегмента.</param>
    [Test]
    [Arguments("/v3/queues/DEV/triggers", "triggers")]
    [Arguments("/v3/queues/DEV/triggers/7", "triggers")]
    [Arguments("/v3/queues/DEV/autoactions/7", "autoactions")]
    [Arguments("/v3/queues/DEV/macros", "macros")]
    [Arguments("/v3/queues/DEV/MACROS/7", "macros")]
    public async Task FindAutomationSegment_MatchesResourcePosition(string path, string expected) =>
        await Assert.That(ExternalEffectsPolicy.FindAutomationSegment(Decoded(path))).IsEqualTo(expected);

    /// <summary>
    /// Percent-encoding не обходит поиск: сегменты сравниваются после декодирования.
    /// </summary>
    /// <param name="path">Путь запроса с закодированным сегментом.</param>
    /// <param name="expected">Ожидаемое каноническое имя сегмента.</param>
    [Test]
    [Arguments("/v3/queues/DEV/%74riggers", "triggers")]
    [Arguments("/v3/queues/DEV/auto%61ctions", "autoactions")]
    [Arguments("/v3/queues/DEV/macro%73", "macros")]
    [Arguments("/v3/%71ueues/DEV/triggers", "triggers")]
    public async Task FindAutomationSegment_DecodesSegments(string path, string expected) =>
        await Assert.That(ExternalEffectsPolicy.FindAutomationSegment(Decoded(path))).IsEqualTo(expected);

    /// <summary>
    /// Совпадение позиционное: имя очереди, совпадающее с именем ресурса автоматизаций,
    /// не превращает запись в очередь в мутацию автоматизации.
    /// </summary>
    /// <param name="path">Путь запроса.</param>
    [Test]
    [Arguments("/v3/queues/TRIGGERS")]
    [Arguments("/v3/queues/TRIGGERS/issues")]
    [Arguments("/v3/queues/MACROS/versions")]
    [Arguments("/v3/issues/TRIGGERS-1/comments")]
    [Arguments("/v3/triggers")]
    public async Task FindAutomationSegment_IgnoresOtherPositions(string path) =>
        await Assert.That(ExternalEffectsPolicy.FindAutomationSegment(Decoded(path))).IsNull();
}
