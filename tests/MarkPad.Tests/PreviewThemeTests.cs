using System.Windows.Controls;
using System.Windows.Media;
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
        foreach (var dark in new[] { true, false, true, false })
        {
            await pane.SetThemeAsync(dark);
            var expected = dark ? Color.FromRgb(13, 17, 23) : Color.FromRgb(232, 235, 239);
            Assert.Equal(expected, Assert.IsType<SolidColorBrush>(pane.Background).Color);
            Assert.Equal(expected, Assert.IsType<SolidColorBrush>(layout.Background).Color);
            Assert.Equal(System.Drawing.Color.FromArgb(expected.R, expected.G, expected.B), browser.DefaultBackgroundColor);
            Assert.Null(browser.CoreWebView2);
        }
    });
}
