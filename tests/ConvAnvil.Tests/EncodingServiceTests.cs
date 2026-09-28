using System.Runtime.InteropServices;
using System.Text;
using ConvAnvil.Services;
using Xunit;

namespace ConvAnvil.Tests;

public sealed class EncodingServiceTests
{
    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-8-bom")]
    [InlineData("utf-16le")]
    [InlineData("utf-16le-no-bom")]
    [InlineData("utf-16be")]
    [InlineData("utf-16be-no-bom")]
    [InlineData("utf-32le")]
    [InlineData("utf-32le-no-bom")]
    [InlineData("utf-32be")]
    [InlineData("utf-32be-no-bom")]
    public void UnicodeRoundTripsSupplementaryCharactersAndControls(string id)
    {
        const string text = "ABC中文🙂\0\r\n\t\u0002\u0003";
        var option = EncodingCatalog.Find(id)!;
        var bytes = EncodingConversionService.Encode(text, option);
        Assert.Equal(text, EncodingConversionService.Decode(bytes, option));
        Assert.Equal(option.EmitBom, EncodingDetectionService.Detect(bytes).BomLength > 0);
    }

    [Theory]
    [InlineData("big5", "繁體中文測試", "C163C5E9A4A4A4E5B4FAB8D5")]
    [InlineData("shift-jis", "日本語", "93FA967B8CEA")]
    [InlineData("windows-1252", "café €", "636166E92080")]
    [InlineData("ascii", "ABC", "414243")]
    public void LegacyEncodingMatchesKnownBytes(string id, string text, string expectedHex)
    {
        var option = EncodingCatalog.Find(id)!;
        var bytes = EncodingConversionService.Encode(text, option);
        Assert.Equal(expectedHex, Convert.ToHexString(bytes));
        Assert.Equal(text, EncodingConversionService.Decode(bytes, option));
    }

    [Fact]
    public void WindowsAnsiUsesSystemAnsiCodePage()
    {
        Assert.Equal((int)GetACP(), EncodingCatalog.WindowsAnsi.CodePage);
        Assert.Equal((int)GetACP(), EncodingCatalog.GetEncoding(EncodingCatalog.WindowsAnsi).CodePage);
    }

    [Fact]
    public void Utf8OutputBomOccursOnlyWhenRequested()
    {
        Assert.Equal("414243", Convert.ToHexString(EncodingConversionService.Encode("ABC", EncodingCatalog.Default)));
        Assert.Equal("EFBBBF414243", Convert.ToHexString(EncodingConversionService.Encode("ABC", EncodingCatalog.Find("utf-8-bom")!)));
    }

    [Fact]
    public void LeadingTextBomIsContentWhenEncoding()
    {
        var option = EncodingCatalog.Find("utf-8-bom")!;
        var bytes = EncodingConversionService.Encode("\uFEFFABC", option);
        Assert.Equal("EFBBBFEFBBBF414243", Convert.ToHexString(bytes));
        Assert.Equal("\uFEFFABC", EncodingConversionService.Decode(bytes, option));
    }

    [Fact]
    public void DecodeCanPreserveBomForByteInspector()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, 0x41];
        Assert.Equal("A", EncodingConversionService.Decode(bytes, EncodingCatalog.Default));
        Assert.Equal("\uFEFFA", EncodingConversionService.Decode(bytes, EncodingCatalog.Default, false));
    }

    [Fact]
    public void ManualMismatchedSourceDoesNotStripAnotherEncodingBom()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, 0x41];
        Assert.Equal("ï»¿A", EncodingConversionService.Decode(bytes, EncodingCatalog.Find("windows-1252")!));
        byte[] utf32 = [0xFF, 0xFE, 0, 0, 0x41, 0, 0, 0];
        Assert.Equal("\0A\0", EncodingConversionService.Decode(utf32, EncodingCatalog.Find("utf-16le")!));
    }

    [Theory]
    [InlineData("utf-16le")]
    [InlineData("utf-16be")]
    [InlineData("utf-32le")]
    [InlineData("utf-32be")]
    public void ExplicitSourceChoiceRoundTripsInitialNulDespiteAmbiguousBom(string id)
    {
        var option = EncodingCatalog.Find(id)!;
        var bytes = EncodingConversionService.Encode("\0A", option);
        Assert.Equal("\0A", EncodingConversionService.Decode(bytes, option));
    }

    [Theory]
    [InlineData("FFFE000041000000", "UTF-32 LE", 12000, 4)]
    [InlineData("0000FEFF00000041", "UTF-32 BE", 12001, 4)]
    [InlineData("FFFE4100", "UTF-16 LE", 1200, 2)]
    [InlineData("FEFF0041", "UTF-16 BE", 1201, 2)]
    [InlineData("EFBBBF41", "UTF-8", 65001, 3)]
    public void BomDetectionChecksLongestSignatureFirst(string hex, string expectedName, int codePage, int bomLength)
    {
        var result = EncodingDetectionService.Detect(Convert.FromHexString(hex));
        Assert.Equal(expectedName, result.BomName);
        Assert.Equal(bomLength, result.BomLength);
        Assert.Equal(codePage, result.Candidate!.CodePage);
        Assert.Null(result.Confidence);
    }

    [Fact]
    public void EmptyAndAsciiDoNotClaimUniqueEncoding()
    {
        foreach (var bytes in new byte[][] { [], [0x41, 0x42, 0x43] })
        {
            var detection = EncodingDetectionService.Detect(bytes);
            Assert.Equal(EncodingCatalog.Default, detection.Candidate);
            Assert.Null(detection.Confidence);
            Assert.Contains("無法判定", detection.Reason);
        }
    }

    [Fact]
    public void MatureDetectorRecognizesUtf8AndReportsItsConfidence()
    {
        var bytes = Encoding.UTF8.GetBytes("繁體中文編碼測試，這是一段測試文字。日本語テスト。English text.");
        var detection = EncodingDetectionService.Detect(bytes);
        Assert.Equal(65001, detection.Candidate!.CodePage);
        Assert.InRange(detection.Confidence!.Value, 0, 1);
        Assert.Contains("UTF.Unknown", detection.Reason);
        Assert.Contains("不是保證", detection.Reason);
    }

    [Theory]
    [InlineData("utf-8", "41FFC3", 1)]
    [InlineData("utf-8", "EFBBBF41FF", 4)]
    [InlineData("utf-8", "41C3", 1)]
    [InlineData("utf-16le", "FFFE410000D8", 4)]
    [InlineData("utf-16be", "FEFF0041D800", 4)]
    [InlineData("utf-32le", "FFFE000000001100", 4)]
    [InlineData("big5", "41A4", 1)]
    public void MalformedInputFailsWithAbsoluteByteOffset(string id, string hex, int offset)
    {
        var exception = Assert.Throws<EncodingConversionException>(() =>
            EncodingConversionService.Decode(Convert.FromHexString(hex), EncodingCatalog.Find(id)!));
        Assert.True(exception.IsByteOffset);
        Assert.Equal(offset, exception.Offset);
        Assert.Contains("位元組位置", exception.Message);
    }

    [Theory]
    [InlineData("ascii", "A中", 1)]
    [InlineData("big5", "A🙂", 1)]
    [InlineData("windows-1252", "A中文", 1)]
    public void UnrepresentableTextFailsBeforeReturningOutput(string id, string text, int offset)
    {
        var exception = Assert.Throws<EncodingConversionException>(() =>
            EncodingConversionService.Encode(text, EncodingCatalog.Find(id)!));
        Assert.False(exception.IsByteOffset);
        Assert.Equal(offset, exception.Offset);
        Assert.Contains("已阻止有損轉換", exception.Message);
    }

    [Theory]
    [InlineData("utf-8", true)]
    [InlineData("utf-16le", true)]
    [InlineData("utf-32be", false)]
    public void UnpairedSurrogateFailsWithCharacterOffset(string id, bool highSurrogate)
    {
        // Construct at runtime: attribute strings are serialized as UTF-8 and
        // cannot reliably carry an unpaired UTF-16 surrogate through metadata.
        var text = "A" + (highSurrogate ? '\uD800' : '\uDC00');
        var exception = Assert.Throws<EncodingConversionException>(() =>
            EncodingConversionService.Encode(text, EncodingCatalog.Find(id)!));
        Assert.False(exception.IsByteOffset);
        Assert.Equal(1, exception.Offset);
    }

    [Theory]
    [InlineData("shift-jis", "¥")]
    [InlineData("shift-jis", "‾")]
    public void LegacyNonRoundTripMappingCannotSilentlyChangeText(string id, string text)
    {
        Assert.Throws<EncodingConversionException>(() =>
            EncodingConversionService.Encode(text, EncodingCatalog.Find(id)!));
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();
}
