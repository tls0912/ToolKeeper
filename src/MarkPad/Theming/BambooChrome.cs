using System.Windows;
using System.Windows.Media;

namespace MarkPad.Theming;

/// <summary>Local bamboo finishes for Hanqing's ink-theme window chrome.</summary>
internal static class BambooChrome
{
    private static readonly Lazy<DrawingBrush> Peeled = new(() => DrawBamboo(false, false));
    private static readonly Lazy<DrawingBrush> PeeledHorizontal = new(() => DrawBamboo(false, true));
    private static readonly Lazy<DrawingBrush> Rind = new(() => DrawBamboo(true, false));
    private static readonly Lazy<DrawingBrush> RindHorizontal = new(() => DrawBamboo(true, true));

    public static void ApplyResources(ResourceDictionary resources, bool ink, bool dark)
    {
        if (ink)
        {
            resources["WindowBackground"] = Solid(dark ? 0x293723 : 0xDCCBA4);
            resources["MutedBrush"] = Solid(dark ? 0xC3CBB4 : 0x414530);
            resources["LineBrush"] = Solid(dark ? 0x586747 : 0xB8A77C);
            resources["HoverBrush"] = Solid(dark ? 0x46583A : 0xD9C99E);
            resources["AccentBrush"] = Solid(dark ? 0xC1D3A5 : 0x4D6541);
        }
        var vertical = ink ? dark ? Rind.Value : Peeled.Value : (Brush)resources["WindowBackground"];
        var horizontal = ink ? dark ? RindHorizontal.Value : PeeledHorizontal.Value : (Brush)resources["WindowBackground"];
        resources["ChromeBackgroundBrush"] = vertical;
        resources["ChromeRailBrush"] = vertical;
        resources["ChromeTitleBrush"] = horizontal;
        resources["ChromeToolbarBrush"] = ink ? horizontal : resources["SurfaceBrush"];
        resources["ChromeTabBrush"] = ink
            ? new LinearGradientBrush(dark ? ColorOf(0x526044) : ColorOf(0xF1E5C8), dark ? ColorOf(0x35442C) : ColorOf(0xDDCAA0), 90)
            : resources["SurfaceBrush"];
        resources["PaperBackgroundBrush"] = ink ? PaperTexture.For(dark) : resources["SurfaceBrush"];
    }

    private static DrawingBrush DrawBamboo(bool dark, bool horizontal)
    {
        const double slatWidth = 38, height = 384, width = slatWidth * 4;
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.DrawRectangle(Solid(dark ? 0x1D2B1B : 0xAE9567), null, new Rect(0, 0, width, height));
            for (var slat = 0; slat < 4; slat++)
            {
                var left = slat * slatWidth;
                var face = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
                    GradientStops = dark
                        ? new GradientStopCollection
                        {
                            new(ColorOf(0x283923), 0), new(ColorOf(0x35482C), 0.12),
                            new(ColorOf(0x435336), 0.32), new(ColorOf(0x34452A), 0.64),
                            new(ColorOf(0x2C3B24), 0.94), new(ColorOf(0x202E1C), 1)
                        }
                        : new GradientStopCollection
                        {
                            new(ColorOf(0xC5AD7C), 0), new(ColorOf(0xE1D0A9), 0.09),
                            new(ColorOf(0xEBDFBD), 0.33), new(ColorOf(0xE4D5AF), 0.64),
                            new(ColorOf(0xD8C397), 0.94), new(ColorOf(0xBFA577), 1)
                        }
                };
                context.DrawRectangle(face, null, new Rect(left + 0.65, 0, slatWidth - 1.3, height));
                // Long, slightly uneven fibers retain their size at every window width.
                for (var fiber = 0; fiber < 11; fiber++)
                {
                    var x = left + 3 + fiber * 2.95;
                    var start = (fiber * 31 + slat * 59) % 83;
                    var geometry = new StreamGeometry();
                    using (var path = geometry.Open())
                    {
                        path.BeginFigure(new Point(x, start), false, false);
                        path.BezierTo(new Point(x - 0.6, start + 96), new Point(x + 0.65, 258), new Point(x - 0.2, height), true, false);
                    }
                    var color = fiber % 3 == 0
                        ? Color.FromArgb(dark ? (byte)22 : (byte)35, 255, 243, 192)
                        : Color.FromArgb(dark ? (byte)23 : (byte)18, dark ? (byte)8 : (byte)108, dark ? (byte)24 : (byte)80, 20);
                    context.DrawGeometry(null, new Pen(new SolidColorBrush(color), fiber % 4 == 0 ? 0.8 : 0.45), geometry);
                }
                // A peeled face has a faint node scar; the rind keeps its raised joint.
                var joint = new[] { 91d, 181d, 273d, 143d }[slat];
                var node = new StreamGeometry();
                using (var path = node.Open())
                {
                    path.BeginFigure(new Point(left + 1, joint), false, false);
                    path.BezierTo(new Point(left + 11, joint + 1.8), new Point(left + 28, joint - 1.2), new Point(left + 37, joint + 0.5), true, false);
                }
                context.DrawGeometry(null, new Pen(new SolidColorBrush(dark ? Color.FromArgb(95, 15, 29, 11) : Color.FromArgb(32, 116, 90, 42)), dark ? 2.6 : 0.75), node);
                context.DrawLine(new Pen(new SolidColorBrush(dark ? Color.FromArgb(42, 203, 211, 151) : Color.FromArgb(80, 255, 247, 211)), 0.7),
                    new Point(left + 2, joint + 2), new Point(left + 36, joint + 2.4));
                context.DrawLine(new Pen(new SolidColorBrush(dark ? Color.FromArgb(37, 219, 227, 172) : Color.FromArgb(100, 255, 251, 224)), 0.6),
                    new Point(left + 1.6, 0), new Point(left + 1.6, height));
            }
        }
        if (horizontal) drawing.Transform = new RotateTransform(90);
        var bounds = horizontal ? new Rect(-height, 0, height, width) : new Rect(0, 0, width, height);
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = bounds, Viewport = new Rect(0, 0, bounds.Width, bounds.Height), Stretch = Stretch.Fill
        };
        brush.Freeze();
        return brush;
    }

    private static Color ColorOf(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    private static SolidColorBrush Solid(int rgb)
    {
        var brush = new SolidColorBrush(ColorOf(rgb));
        brush.Freeze();
        return brush;
    }
}
