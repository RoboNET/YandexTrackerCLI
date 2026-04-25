namespace YandexTrackerCLI.Tests.Input;

using TUnit.Core;
using Core.Api.Errors;
using YandexTrackerCLI.Input;

public sealed class JsonBodyReaderTests
{
    [Test]
    public async Task ReadFromFile_ReturnsRawContent_Verbatim()
    {
        var path = Path.Combine(Path.GetTempPath(), "jbr-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, """{"a":1,"b":[2,3]}""");

        var result = JsonBodyReader.Read(filePath: path, fromStdin: false, stdinReader: null);

        await Assert.That(result).IsEqualTo("""{"a":1,"b":[2,3]}""");
    }

    [Test]
    public async Task ReadFromStdin_UsesProvidedReader()
    {
        var reader = new StringReader("""{"x":true}""");
        var result = JsonBodyReader.Read(filePath: null, fromStdin: true, stdinReader: reader);
        await Assert.That(result).IsEqualTo("""{"x":true}""");
    }

    [Test]
    public async Task Read_BothFileAndStdin_Throws_InvalidArgs()
    {
        var path = Path.Combine(Path.GetTempPath(), "jbr-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, "{}");

        var ex = Assert.Throws<TrackerException>(() =>
            JsonBodyReader.Read(filePath: path, fromStdin: true, stdinReader: new StringReader("{}")));
        await Assert.That(ex.Code).IsEqualTo(ErrorCode.InvalidArgs);
    }

    [Test]
    public async Task Read_NeitherProvided_ReturnsNull()
    {
        var result = JsonBodyReader.Read(filePath: null, fromStdin: false, stdinReader: null);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Read_InvalidJson_ThrowsInvalidArgs()
    {
        var path = Path.Combine(Path.GetTempPath(), "jbr-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, "{not json");

        var ex = Assert.Throws<TrackerException>(() =>
            JsonBodyReader.Read(filePath: path, fromStdin: false, stdinReader: null));
        await Assert.That(ex.Code).IsEqualTo(ErrorCode.InvalidArgs);
    }

    [Test]
    public async Task Read_FileMissing_ThrowsInvalidArgs()
    {
        var ex = Assert.Throws<TrackerException>(() =>
            JsonBodyReader.Read(filePath: "/tmp/does-not-exist-" + Guid.NewGuid(), fromStdin: false, stdinReader: null));
        await Assert.That(ex.Code).IsEqualTo(ErrorCode.InvalidArgs);
    }

    [Test]
    public async Task Read_StdinWithoutReader_ThrowsInvalidArgs()
    {
        var ex = Assert.Throws<TrackerException>(() =>
            JsonBodyReader.Read(filePath: null, fromStdin: true, stdinReader: null));
        await Assert.That(ex.Code).IsEqualTo(ErrorCode.InvalidArgs);
    }

    [Test]
    public async Task Read_EmptyStdin_ThrowsInvalidArgs()
    {
        var ex = Assert.Throws<TrackerException>(() =>
            JsonBodyReader.Read(filePath: null, fromStdin: true, stdinReader: new StringReader("")));
        await Assert.That(ex.Code).IsEqualTo(ErrorCode.InvalidArgs);
    }
}
