namespace YandexTrackerCLI.Tests.Input;

using TUnit.Core;
using YandexTrackerCLI.Input;
using static YandexTrackerCLI.Input.JsonBodyMerger;

public sealed class JsonBodyReaderReadAndMergeTests
{
    [Test]
    public async Task ReadAndMerge_FileWithOverride_MergesBoth()
    {
        var path = Path.Combine(Path.GetTempPath(), "yt-rm-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, """{"name":"old","x":1}""");
        try
        {
            var ov = new (string, OverrideValue)[] { ("name", OverrideValue.Of("new")) };
            var result = JsonBodyReader.ReadAndMerge(path, fromStdin: false, stdinReader: null, ov)!;

            using var doc = System.Text.Json.JsonDocument.Parse(result);
            await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("new");
            await Assert.That(doc.RootElement.GetProperty("x").GetInt32()).IsEqualTo(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ReadAndMerge_NoSource_NoOverrides_ReturnsNull()
    {
        var result = JsonBodyReader.ReadAndMerge(null, false, null, Array.Empty<(string, OverrideValue)>());
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadAndMerge_StdinWithOverride_MergesBoth()
    {
        using var sr = new StringReader("""{"a":1}""");
        var ov = new (string, OverrideValue)[] { ("b", OverrideValue.Of("v")) };
        var result = JsonBodyReader.ReadAndMerge(null, fromStdin: true, sr, ov)!;
        using var doc = System.Text.Json.JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("a").GetInt32()).IsEqualTo(1);
        await Assert.That(doc.RootElement.GetProperty("b").GetString()).IsEqualTo("v");
    }
}
