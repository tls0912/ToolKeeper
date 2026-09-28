using System.Globalization;
using System.Text;
using ConvAnvil.Services;
using Xunit;

namespace ConvAnvil.Tests;

public class ByteNotationTests
{
    [Theory]
    [InlineData("41 42 43 E4 B8 AD E6 96 87")]
    [InlineData("0x41 0x42 0x43 0xe4 0xb8 0xad 0xe6 0x96 0x87")]
    [InlineData("41,42,43,E4,B8,AD,E6,96,87")]
    [InlineData("\\x41\\x42\\x43\\xE4\\xB8\\xAD\\xE6\\x96\\x87")]
    [InlineData("\t41, 0x42\r\n\\x43 E4, B8\tAD E6 96 87  ")]
    public void HexNotationsDecodeTheProductExample(string input)
    {
        foreach (var format in new[] { ByteFormat.Hex, ByteFormat.HexPrefix, ByteFormat.HexComma, ByteFormat.HexEscape })
        {
            Assert.Equal("ABC中文", Encoding.UTF8.GetString(ByteNotation.Parse(input, format)));
        }
    }

    [Theory]
    [InlineData(ByteFormat.Hex, "00 02 03 09 0A 0D 41 FF")]
    [InlineData(ByteFormat.HexPrefix, "0x00 0x02 0x03 0x09 0x0A 0x0D 0x41 0xFF")]
    [InlineData(ByteFormat.HexComma, "00,02,03,09,0A,0D,41,FF")]
    [InlineData(ByteFormat.HexEscape, "\\x00\\x02\\x03\\x09\\x0A\\x0D\\x41\\xFF")]
    [InlineData(ByteFormat.Decimal, "0 2 3 9 10 13 65 255")]
    [InlineData(ByteFormat.Binary, "00000000 00000010 00000011 00001001 00001010 00001101 01000001 11111111")]
    public void FormattingUsesCanonicalUnambiguousByteNotation(ByteFormat format, string expected)
    {
        byte[] bytes = [0, 2, 3, 9, 10, 13, 65, 255];
        Assert.Equal(expected, ByteNotation.Format(bytes, format));
        Assert.Equal(bytes, ByteNotation.Parse(expected, format));
    }

    [Fact]
    public void EveryByteRoundTripsInEveryFormat()
    {
        var bytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        foreach (var format in Enum.GetValues<ByteFormat>())
        {
            Assert.Equal(bytes, ByteNotation.Parse(ByteNotation.Format(bytes, format), format));
        }
    }

    [Theory]
    [InlineData("4", 1)]
    [InlineData("GG", 0)]
    [InlineData("41 4G", 4)]
    [InlineData("4142", 2)]
    [InlineData("0x4Z", 3)]
    [InlineData("\\x41\\q42", 5)]
    [InlineData("41,,42", 3)]
    [InlineData("41,", 2)]
    [InlineData(",41", 0)]
    [InlineData("41 ;42", 3)]
    [InlineData("0x41 42x", 7)]
    [InlineData("41\\x42", 2)]
    [InlineData("41, ,42", 4)]
    public void MalformedHexReportsTheExactOffsetWithoutSkippingInput(string input, int offset)
    {
        var exception = Assert.Throws<ByteParseException>(() => ByteNotation.Parse(input, ByteFormat.Hex));
        Assert.Equal(offset, exception.Offset);
        Assert.StartsWith($"第 {offset + 1} 個字元：", exception.Message);
    }

    [Theory]
    [InlineData("256", ByteFormat.Decimal, 2)]
    [InlineData("-1", ByteFormat.Decimal, 0)]
    [InlineData("65 2a", ByteFormat.Decimal, 4)]
    [InlineData("65,,", ByteFormat.Decimal, 3)]
    [InlineData("0x41", ByteFormat.Decimal, 1)]
    [InlineData("0100001", ByteFormat.Binary, 7)]
    [InlineData("01020001", ByteFormat.Binary, 3)]
    [InlineData("100000000", ByteFormat.Binary, 8)]
    public void DecimalAndBinaryHaveStrictRangeWidthAndSeparators(string input, ByteFormat format, int offset)
    {
        Assert.Equal(offset, Assert.Throws<ByteParseException>(() => ByteNotation.Parse(input, format)).Offset);
    }

    [Fact]
    public void NumericFormatsNeverGuessTheRadix()
    {
        Assert.Equal(new byte[] { 0x65 }, ByteNotation.Parse("65", ByteFormat.Hex));
        Assert.Equal(new byte[] { 65 }, ByteNotation.Parse("65", ByteFormat.Decimal));
        Assert.Throws<ByteParseException>(() => ByteNotation.Parse("65", ByteFormat.Binary));
        Assert.Equal(new byte[] { 65, 0, 255 }, ByteNotation.Parse("65, 0,255", ByteFormat.Decimal));
    }

    [Fact]
    public void EmptyInputIsEmptyInEveryFormat()
    {
        foreach (var format in Enum.GetValues<ByteFormat>())
        {
            Assert.Empty(ByteNotation.Parse("\t \r\n", format));
            Assert.Equal(string.Empty, ByteNotation.Format([], format));
        }
    }

    [Fact]
    public void DecimalOutputIsCultureIndependent()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.Equal("0 65 255", ByteNotation.Format([0, 65, 255], ByteFormat.Decimal));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void InvalidEnumsAndNullInputsFailExplicitly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteNotation.Parse("41", (ByteFormat)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteNotation.Format([65], (ByteFormat)99));
        Assert.Throws<ArgumentNullException>(() => ByteNotation.Parse(null!, ByteFormat.Hex));
        Assert.Throws<ArgumentNullException>(() => ByteNotation.Format(null!, ByteFormat.Hex));
    }
}
