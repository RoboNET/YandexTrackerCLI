namespace YandexTrackerCLI.Core.Tests.Auth;

using System.Text;
using TUnit.Core;
using YandexTrackerCLI.Core.Auth;

public sealed class Base64UrlTests
{
    // RFC 4648 §10 test vectors
    [Test]
    [Arguments("", "")]
    [Arguments("f", "Zg")]
    [Arguments("fo", "Zm8")]
    [Arguments("foo", "Zm9v")]
    [Arguments("foob", "Zm9vYg")]
    [Arguments("fooba", "Zm9vYmE")]
    [Arguments("foobar", "Zm9vYmFy")]
    public async Task EncodeUtf8_StripsPaddingAndReplacesChars(string input, string expected)
    {
        var actual = Base64Url.Encode(Encoding.UTF8.GetBytes(input));
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task Encode_ReplacesPlusWithDash_SlashWithUnderscore_NoPadding()
    {
        // bytes that in plain base64 contain both '+' and '/'
        var bytes = new byte[] { 0xFB, 0xFF, 0xBF };
        var result = Base64Url.Encode(bytes);
        await Assert.That(result).DoesNotContain("+");
        await Assert.That(result).DoesNotContain("/");
        await Assert.That(result).DoesNotContain("=");
    }

    [Test]
    public async Task Encode_EmptySpan_ReturnsEmptyString()
    {
        await Assert.That(Base64Url.Encode(ReadOnlySpan<byte>.Empty)).IsEqualTo("");
    }
}
