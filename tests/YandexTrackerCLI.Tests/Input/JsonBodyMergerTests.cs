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

    [Test]
    public async Task Merge_NullRaw_WithOverrides_BuildsObjectFromOverrides()
    {
        var ov = new (string, OverrideValue)[]
        {
            ("name", OverrideValue.Of("hello")),
            ("active", OverrideValue.Of(true)),
            ("count", OverrideValue.Of(42L)),
        };
        var result = Merge(null, ov)!;

        using var doc = System.Text.Json.JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("hello");
        await Assert.That(doc.RootElement.GetProperty("active").GetBoolean()).IsTrue();
        await Assert.That(doc.RootElement.GetProperty("count").GetInt64()).IsEqualTo(42L);
    }

    [Test]
    public async Task Merge_RawObject_OverridesExistingKey()
    {
        var raw = """{"name":"old","active":false}""";
        var ov = new (string, OverrideValue)[] { ("name", OverrideValue.Of("new")) };

        var result = Merge(raw, ov)!;
        using var doc = System.Text.Json.JsonDocument.Parse(result);

        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("new");
        await Assert.That(doc.RootElement.GetProperty("active").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task Merge_RawObject_AppendsNewKey()
    {
        var raw = """{"name":"x"}""";
        var ov = new (string, OverrideValue)[] { ("active", OverrideValue.Of(true)) };

        var result = Merge(raw, ov)!;
        using var doc = System.Text.Json.JsonDocument.Parse(result);

        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("x");
        await Assert.That(doc.RootElement.GetProperty("active").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task Merge_RawObject_PreservesNestedObjects()
    {
        var raw = """{"conditions":{"a":{"b":[1,2,3]}}}""";
        var result = Merge(raw, Array.Empty<(string, OverrideValue)>())!;

        using var doc = System.Text.Json.JsonDocument.Parse(result);
        var inner = doc.RootElement.GetProperty("conditions").GetProperty("a").GetProperty("b");
        await Assert.That(inner.GetArrayLength()).IsEqualTo(3);
        await Assert.That(inner[2].GetInt32()).IsEqualTo(3);
    }

    [Test]
    public async Task Merge_RawArray_Throws()
    {
        var raw = """[1,2,3]""";
        Core.Api.Errors.TrackerException? caught = null;
        try
        {
            Merge(raw, Array.Empty<(string, OverrideValue)>());
        }
        catch (Core.Api.Errors.TrackerException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Code).IsEqualTo(Core.Api.Errors.ErrorCode.InvalidArgs);
    }

    [Test]
    public async Task Merge_NullValueOverride_WritesJsonNull()
    {
        var raw = """{"name":"x"}""";
        var ov = new (string, OverrideValue)[] { ("name", OverrideValue.Null) };

        var result = Merge(raw, ov)!;
        using var doc = System.Text.Json.JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("name").ValueKind)
            .IsEqualTo(System.Text.Json.JsonValueKind.Null);
    }

    [Test]
    public async Task Merge_InvalidJsonRaw_ThrowsInvalidArgs()
    {
        Core.Api.Errors.TrackerException? caught = null;
        try
        {
            Merge("not json", Array.Empty<(string, OverrideValue)>());
        }
        catch (Core.Api.Errors.TrackerException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Code).IsEqualTo(Core.Api.Errors.ErrorCode.InvalidArgs);
    }

    [Test]
    public async Task OverrideValueOf_NullString_Throws()
    {
        ArgumentNullException? caught = null;
        try
        {
            OverrideValue.Of((string)null!);
        }
        catch (ArgumentNullException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
    }

    [Test]
    public async Task Merge_DuplicateOverrideKey_LastWins()
    {
        var ov = new (string, OverrideValue)[]
        {
            ("name", OverrideValue.Of("first")),
            ("name", OverrideValue.Of("last")),
        };
        var result = Merge(null, ov)!;
        using var doc = System.Text.Json.JsonDocument.Parse(result);
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("last");
    }
}
