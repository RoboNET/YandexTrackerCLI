namespace YandexTrackerCLI.Tests.Input;

using TUnit.Core;
using YandexTrackerCLI.Input;
using static YandexTrackerCLI.Input.JsonBodyMerger;

public sealed class JsonBodyMergerTests
{
    [Test]
    public async Task Merge_NullRaw_NoOverrides_ReturnsNull()
    {
        var result = Merge(null, Array.Empty<(string, OverrideValue)>());
        await Assert.That(result).IsNull();
    }
}
