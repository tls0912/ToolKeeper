using System.Windows.Controls;
using System.Windows.Media;
using AngleSharp.Html.Parser;
using MarkPad.Rendering;
using Microsoft.Web.WebView2.Wpf;
using Xunit;

namespace MarkPad.Tests;

public sealed class PreviewThemeTests
{
    [Fact]
    public Task ThemeSwitchUpdatesBothNativeAndWpfBackgroundsBeforeBrowserInitialization() => StaTest.Run(async () =>
    {
        using var pane = new PreviewPane();
        var layout = Assert.IsType<Grid>(pane.Content);
        var browser = Assert.IsType<WebView2CompositionControl>(layout.Children[0]);
        // Include transitions between both ink variants and the original palettes so a retained
        // native canvas cannot keep the previous theme's background or notice foreground.
        foreach (var (dark, ink, backgroundHex, foregroundHex) in new[]
        {
            (true, false, "#0D1117", "#E6EDF3"),
            (true, true, "#1F2421", "#E5E1D5"),
            (false, true, "#F3F0E7", "#2C302D"),
            (false, false, "#E8EBEF", "#24292F"),
            (true, true, "#1F2421", "#E5E1D5"),
            (true, false, "#0D1117", "#E6EDF3"),
            (false, true, "#F3F0E7", "#2C302D")
        })
        {
            await pane.SetThemeAsync(dark, ink);
            var expected = (Color)ColorConverter.ConvertFromString(backgroundHex);
            Assert.Equal(expected, Assert.IsType<SolidColorBrush>(pane.Background).Color);
            Assert.Equal(expected, Assert.IsType<SolidColorBrush>(layout.Background).Color);
            Assert.Equal(System.Drawing.Color.FromArgb(expected.R, expected.G, expected.B), browser.DefaultBackgroundColor);
            var notice = Assert.IsType<TextBlock>(layout.Children[1]);
            var foreground = (Color)ColorConverter.ConvertFromString(foregroundHex);
            Assert.Equal(foreground, Assert.IsType<SolidColorBrush>(notice.Foreground).Color);
            Assert.Null(browser.CoreWebView2);
        }
    });

    [Theory]
    [InlineData(false, false, "light")]
    [InlineData(true, false, "dark")]
    [InlineData(false, true, "ink")]
    [InlineData(true, true, "ink-dark")]
    public void RenderedThemePreservesDarkModeForBothInkPalettes(bool dark, bool ink, string theme)
    {
        var html = new MarkdownRenderer().Render("# 墨色筆記\n\n清楚閱讀。", new PreviewOptions(Dark: dark, Ink: ink));
        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal(theme, document.DocumentElement.GetAttribute("data-theme"));
        Assert.Equal(ink, document.DocumentElement.ClassList.Contains("ink"));
        Assert.Equal(dark, document.DocumentElement.ClassList.Contains("dark"));
        Assert.Equal(!dark, document.DocumentElement.ClassList.Contains("light"));
        Assert.Equal("墨色筆記", document.QuerySelector("#document h1")!.TextContent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InkFontPreferenceCannotEscapeTheOfflinePreviewShell(bool dark)
    {
        var html = new MarkdownRenderer().Render("# 水墨", new PreviewOptions(Dark: dark, Ink: true,
            FontFamily: "DFKai-SB';}</style><script src='https://example.invalid/a.js'></script>"));
        var document = new HtmlParser().ParseDocument(html);
        var script = Assert.Single(document.QuerySelectorAll("script"));
        Assert.False(script.HasAttribute("src"));
        Assert.Single(document.QuerySelectorAll("style"));
        Assert.Empty(document.QuerySelectorAll("link"));
        Assert.Contains("connect-src 'none'", document.QuerySelector("meta[http-equiv='Content-Security-Policy']")!.GetAttribute("content")!);
    }
}
