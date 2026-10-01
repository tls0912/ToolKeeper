using System.Collections;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace ToolKeeper.UI.Tests;

public sealed class BambooChromeTests
{
    [Fact]
    public Task ReturningToPlainThemePreservesProductResourcesAndOtherOwners() => StaTest.Run(() =>
    {
        var productResource = new object();
        var first = new ResourceDictionary { ["ProductSpecificResource"] = productResource };
        var second = new ResourceDictionary();
        ApplyTheme(first, "Ink");
        ApplyTheme(second, "InkDark");
        var untouched = second.Cast<DictionaryEntry>().ToDictionary(entry => entry.Key, entry => entry.Value);
        ApplyTheme(first, "InkDark");
        ApplyTheme(first, "Light");
        Assert.Same(productResource, first["ProductSpecificResource"]);
        foreach (var (key, value) in untouched) Assert.Same(value, second[key]);
        foreach (var key in new[] { "ChromeBackgroundBrush", "ChromeRailBrush", "ChromeTitleBrush" })
            Assert.Same(first["WindowBackground"], first[key]);
        foreach (var key in new[] { "ChromeToolbarBrush", "ChromeTabBrush", "PaperBackgroundBrush" })
            Assert.Same(first["SurfaceBrush"], first[key]);
        Assert.Equal(UiTheme.Palette(false, false).Accent, Assert.IsType<SolidColorBrush>(first["AccentBrush"]).Color);
    });

    [Theory]
    [InlineData("Ink")]
    [InlineData("InkDark")]
    public Task BambooAndPaperRetainTheirTextureScaleWhenTheWindowGrows(string theme) => StaTest.Run(() =>
    {
        var resources = new ResourceDictionary();
        ApplyTheme(resources, theme);
        foreach (var key in new[] { "ChromeBackgroundBrush", "ChromeTitleBrush", "PaperBackgroundBrush" })
        {
            var brush = Assert.IsType<DrawingBrush>(resources[key]);
            Assert.True(brush.IsFrozen);
            Assert.Equal(TileMode.Tile, brush.TileMode);
            Assert.Equal(BrushMappingMode.Absolute, brush.ViewportUnits);
            Assert.Equal(BrushMappingMode.Absolute, brush.ViewboxUnits);
            Assert.True(brush.Viewport.Width > 0 && brush.Viewport.Height > 0);
            Assert.Equal(brush.Viewbox.Size, brush.Viewport.Size);
            var initial = RenderCrop(brush, 240, 180);
            Assert.True(initial.Chunk(4).Select(pixel => BitConverter.ToInt32(pixel)).Distinct().Count() > 4,
                $"{key} must render texture rather than a flat fill.");
            Assert.Equal(initial, RenderCrop(brush, 720, 480));
        }
    });

    private static void ApplyTheme(ResourceDictionary resources, string theme)
    {
        UiTheme.ApplyResources(resources, theme);
        BambooChrome.ApplyResources(resources, UiTheme.IsInk(theme), UiTheme.IsDark(theme));
    }

    private static byte[] RenderCrop(Brush brush, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        const int cropWidth = 192, cropHeight = 128, stride = cropWidth * 4;
        var pixels = new byte[stride * cropHeight];
        bitmap.CopyPixels(new Int32Rect(0, 0, cropWidth, cropHeight), pixels, stride, 0);
        return pixels;
    }
}