using PBLEngine;
using Xunit;

namespace PBLEngine.Tests;

public class BuildCodecTests
{
    private const string SampleXml = "<PathOfBuilding2><Build level=\"1\" /></PathOfBuilding2>";

    [Fact]
    public void Encode_ProducesNonEmptyString()
    {
        var code = BuildCodec.Encode(SampleXml);
        Assert.False(string.IsNullOrEmpty(code));
    }

    [Fact]
    public void Encode_ProducesBase64UrlSafeChars()
    {
        var code = BuildCodec.Encode(SampleXml);
        Assert.DoesNotContain("+", code);
        Assert.DoesNotContain("/", code);
        Assert.DoesNotContain("=", code);
    }

    [Fact]
    public void Decode_AfterEncode_ReturnsOriginalXml()
    {
        var code = BuildCodec.Encode(SampleXml);
        var decoded = BuildCodec.Decode(code);

        Assert.Equal(SampleXml, decoded);
    }

    [Fact]
    public void Decode_InvalidCode_ReturnsNull()
    {
        var result = BuildCodec.Decode("not-a-valid-code!!!");
        Assert.Null(result);
    }

    [Fact]
    public void Decode_EmptyString_ReturnsNull()
    {
        var result = BuildCodec.Decode("");
        Assert.Null(result);
    }

    [Fact]
    public void Encode_Decode_LargeXml_RoundTrips()
    {
        var xml = "<PathOfBuilding2>" + new string('x', 10_000) + "</PathOfBuilding2>";
        var code = BuildCodec.Encode(xml);
        var decoded = BuildCodec.Decode(code);

        Assert.Equal(xml, decoded);
    }

    [Fact]
    public void Encode_LargeXml_IsSmallerThanInput()
    {
        var xml = "<PathOfBuilding2>" + new string('a', 10_000) + "</PathOfBuilding2>";
        var code = BuildCodec.Encode(xml);

        Assert.True(code.Length < xml.Length, "Compressed code should be smaller than raw XML");
    }

    [Fact]
    public void LooksLikeCode_ShortString_ReturnsFalse()
    {
        Assert.False(BuildCodec.LooksLikeCode("abc"));
    }

    [Fact]
    public void LooksLikeCode_HttpUrl_ReturnsFalse()
    {
        Assert.False(BuildCodec.LooksLikeCode("https://pastebin.com/abc123"));
    }

    [Fact]
    public void LooksLikeCode_ValidCode_ReturnsTrue()
    {
        var code = BuildCodec.Encode(SampleXml);
        Assert.True(BuildCodec.LooksLikeCode(code));
    }
}
