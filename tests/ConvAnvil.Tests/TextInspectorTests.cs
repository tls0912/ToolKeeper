using ConvAnvil.Services;
using Xunit;

namespace ConvAnvil.Tests;

public class TextInspectorTests
{
    [Theory]
    [InlineData("", "None", 0, 0, 0)]
    [InlineData("ABC", "None", 0, 0, 0)]
    [InlineData("a\r\nb\r\n", "CRLF", 2, 0, 0)]
    [InlineData("a\nb\n", "LF", 0, 2, 0)]
    [InlineData("a\rb\r", "CR", 0, 0, 2)]
    [InlineData("a\r\nb\nc\r", "Mixed", 1, 1, 1)]
    [InlineData("\r\r\n\n", "Mixed", 1, 1, 1)]
    public void LineEndingCountsDoNotDoubleCountCrLf(string input, string label, int crlf, int lf, int cr)
    {
        var result = TextInspector.Inspect(input);
        Assert.Equal(label, result.LineEnding);
        Assert.Equal(crlf, result.CrLfCount);
        Assert.Equal(lf, result.LfCount);
        Assert.Equal(cr, result.CrCount);
    }

    [Fact]
    public void CountsScalarsSeparatelyFromUtf16AndCombiningGraphemes()
    {
        var result = TextInspector.Inspect("A中文😀e\u0301");
        Assert.Equal(7, result.Utf16Length);
        Assert.Equal(6, result.UnicodeScalarCount);
        Assert.Equal(6, result.CharacterCount);
        Assert.Equal(0, result.InvalidSurrogateCount);
    }

    [Fact]
    public void UnpairedSurrogatesAreReportedAndNeverCountedAsValidScalars()
    {
        var result = TextInspector.Inspect("\uD800A\uDC00😀");
        Assert.Equal(5, result.Utf16Length);
        Assert.Equal(2, result.UnicodeScalarCount);
        Assert.Equal(2, result.InvalidSurrogateCount);
        Assert.True(result.HasInvalidUtf16);
        Assert.Equal("⟦INVALID U+D800⟧A⟦INVALID U+DC00⟧😀", TextInspector.VisualizeControls("\uD800A\uDC00😀"));
    }

    [Fact]
    public void CountsEngineeringControlsAndOtherControlBytes()
    {
        var result = TextInspector.Inspect("\0\0\u0002\u0003\t\r\n\r\n\n\r\u0007\u007F\u0085");
        Assert.Equal(2, result.NulCount);
        Assert.Equal(1, result.StxCount);
        Assert.Equal(1, result.EtxCount);
        Assert.Equal(1, result.TabCount);
        Assert.Equal(2, result.CrLfCount);
        Assert.Equal(1, result.LfCount);
        Assert.Equal(1, result.CrCount);
        Assert.Equal(3, result.OtherControlCount);
        Assert.Equal(14, result.ControlCount);
    }

    [Fact]
    public void LiteralEscapeTextCannotImitateActualControls()
    {
        Assert.Equal("ABC⟦CR⟧⟦LF⟧\n", TextInspector.VisualizeControls("ABC\r\n"));
        Assert.Equal(@"ABC\\r\\n", TextInspector.VisualizeControls(@"ABC\r\n"));
        Assert.Equal(@"\⟦CR⟧\⟦LF⟧", TextInspector.VisualizeControls("⟦CR⟧⟦LF⟧"));
        Assert.NotEqual(TextInspector.VisualizeControls("\0"), TextInspector.VisualizeControls("⟦NUL⟧"));
        Assert.Equal("⟦NUL⟧⟦STX⟧⟦ETX⟧⟦TAB⟧⟦U+0007⟧", TextInspector.VisualizeControls("\0\u0002\u0003\t\u0007"));
    }

    [Fact]
    public void BareCrAndLfRemainReadableAsSeparateLines()
    {
        Assert.Equal("a⟦CR⟧\nb⟦LF⟧\nc⟦CR⟧⟦LF⟧\nd", TextInspector.VisualizeControls("a\rb\nc\r\nd"));
    }

    [Theory]
    [InlineData(false, "a\nb\nc\n\nd")]
    [InlineData(true, "a\r\nb\r\nc\r\n\r\nd")]
    public void NormalizingMixedEndingsPreservesBlankLinesAndIsIdempotent(bool crlf, string expected)
    {
        var result = TextInspector.NormalizeLineEndings("a\r\nb\rc\n\r\nd", crlf);
        Assert.Equal(expected, result);
        Assert.Equal(result, TextInspector.NormalizeLineEndings(result, crlf));
    }

    [Fact]
    public void NormalizationDoesNotRewriteOtherControlsOrLiteralEscapeSequences()
    {
        const string input = "\\r\\n\0\t\u0085\u2028\u2029\v\f";
        Assert.Equal(input, TextInspector.NormalizeLineEndings(input, true));
        Assert.Equal(input, TextInspector.NormalizeLineEndings(input, false));
        Assert.Equal("None", TextInspector.Inspect(input).LineEnding);
    }
}
