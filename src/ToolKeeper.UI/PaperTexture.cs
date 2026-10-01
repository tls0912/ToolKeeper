using System.Windows;
using System.Windows.Media;

namespace ToolKeeper.UI;

/// <summary>Sparse paper fibers for native ink-theme reading surfaces.</summary>
public static class PaperTexture
{
    private static readonly Lazy<DrawingBrush> Light = new(() => Draw(false));
    private static readonly Lazy<DrawingBrush> Dark = new(() => Draw(true));

    public static DrawingBrush For(bool dark) => dark ? Dark.Value : Light.Value;

    private static DrawingBrush Draw(bool dark)
    {
        // Keep the tile in device-independent pixels: resizing a pane must not stretch the fibers.
        const double width = 421, height = 347;
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(31, 36, 33) : Color.FromRgb(243, 240, 231)),
                null, new Rect(0, 0, width, height));
            var fiber = dark ? Color.FromArgb(54, 216, 208, 178) : Color.FromArgb(60, 120, 104, 73);
            var softFiber = dark ? Color.FromArgb(35, 216, 208, 178) : Color.FromArgb(40, 120, 104, 73);
            var highlight = dark ? Color.FromArgb(64, 0, 0, 0) : Color.FromArgb(170, 255, 255, 255);
            // Unequal lengths and scattered centers leave open paper between individual fibers.
            Ellipse(context, fiber, 51, 39, 27, 1.15);
            Ellipse(context, softFiber, 236, 62, 0.9, 19);
            Ellipse(context, highlight, 337, 113, 23, 1.2);
            Ellipse(context, softFiber, 144, 158, 18, 0.9);
            Ellipse(context, fiber, 363, 236, 31, 1);
            Ellipse(context, highlight, 83, 272, 0.8, 17);
            Ellipse(context, softFiber, 256, 300, 21, 1.05);
            Ellipse(context, softFiber, 29, 190, 0.75, 13);
            Ellipse(context, highlight, 289, 196, 15, 0.85);
            var pulp = dark ? Color.FromArgb(5, 162, 190, 169) : Color.FromArgb(5, 139, 121, 89);
            Ellipse(context, pulp, 123, 83, 78, 53);
            Ellipse(context, pulp, 321, 269, 63, 71);
        }
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, width, height),
            Viewport = new Rect(0, 0, width, height),
            Stretch = Stretch.Fill
        };
        brush.Freeze();
        return brush;
    }

    private static void Ellipse(DrawingContext context, Color color, double x, double y, double rx, double ry)
    {
        var brush = new RadialGradientBrush
        {
            GradientStops = new GradientStopCollection
            {
                new(color, 0), new(color, 0.12),
                new(Color.FromArgb((byte)(color.A / 3), color.R, color.G, color.B), 0.6),
                new(Color.FromArgb(0, color.R, color.G, color.B), 1)
            }
        };
        context.DrawEllipse(brush, null, new Point(x, y), rx, ry);
    }
}
