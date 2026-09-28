// Shared vector source for the Hanqing SVG, ICO, MSIX logos and Store artwork.
// Windows PowerShell 5.1 Add-Type compatible; uses System.Drawing only.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;

public static class HanqingIconRenderer
{
    private static Color C(string hex) { return ColorTranslator.FromHtml(hex); }
    private static Color A(int alpha, string hex) { return Color.FromArgb(alpha, C(hex)); }
    private static string F(float value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }

    /// <summary>Caller owns and disposes the returned, transparent PNG-ready bitmap.</summary>
    public static Bitmap CreateBitmap(int size)
    {
        if (size < 1 || size > 4096) throw new ArgumentOutOfRangeException("size");
        int supersampling = size <= 600 ? 4 : 2;
        using (Bitmap large = new Bitmap(size * supersampling, size * supersampling, PixelFormat.Format32bppArgb))
        using (Graphics graphics = Graphics.FromImage(large))
        {
            graphics.Clear(Color.Transparent);
            Draw(graphics, 0, 0, size * supersampling);
            Bitmap output = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics downsample = Graphics.FromImage(output))
                {
                    downsample.CompositingMode = CompositingMode.SourceCopy;
                    downsample.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    downsample.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    downsample.DrawImage(large, new Rectangle(0, 0, size, size));
                }
                output.SetResolution(96, 96);
                return output;
            }
            catch { output.Dispose(); throw; }
        }
    }

    /// <summary>Paints the existing blue brand with its bamboo-built M, preserving caller state.</summary>
    public static void Draw(Graphics graphics, float x, float y, float size)
    {
        if (graphics == null) throw new ArgumentNullException("graphics");
        if (size <= 0 || float.IsNaN(size) || float.IsInfinity(size)) throw new ArgumentOutOfRangeException("size");
        GraphicsState state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TranslateTransform(x, y); graphics.ScaleTransform(size / 256, size / 256);
            Paint(new GdiCanvas(graphics));
        }
        finally { graphics.Restore(state); }
    }

    /// <summary>Self-contained SVG made by the identical primitive calls used by Draw.</summary>
    public static string CreateSvg()
    {
        SvgCanvas canvas = new SvgCanvas(); Paint(canvas); return canvas.Finish();
    }

    private static void Paint(Canvas c)
    {
        c.GradientRoundRect(10, 10, 236, 236, 50,
            new Color[] { C("#2784EE"), C("#0958C2") }, new float[] { 0, 1 }, true);
        // Four complete rind-covered stalks: two uprights and two inward diagonals.
        // A wide, simple silhouette keeps M legible at the 32/48-pixel shell sizes.
        Stalk(c, 59, 80, 59, 171, 26, 0);
        Stalk(c, 197, 80, 197, 171, 26, 1);
        Stalk(c, 63, 83, 128, 148, 25, 2);
        Stalk(c, 193, 83, 128, 148, 25, 3);
        c.RoundRect(88, 196, 80, 8, 4, Color.FromArgb(128, 255, 255, 255));
    }

    private static void Stalk(Canvas c, float x1, float y1, float x2, float y2, float width, int variant)
    {
        float dx = x2 - x1, dy = y2 - y1;
        float length = (float)Math.Sqrt(dx * dx + dy * dy);
        float angle = (float)(Math.Atan2(dy, dx) * 180 / Math.PI - 90);
        float left = -width / 2, cap = 5.2f;
        c.Push(x1, y1, angle);
        // Soft contact shadow separates diagonal joints without outlining the entire logo.
        c.RoundRect(left + 1, -cap + 2.3f, width + .5f, length + cap * 2, cap, A(65, "#063F73"));
        c.GradientRoundRect(left, -cap, width, length + cap * 2, cap,
            new Color[] { C("#365B31"), C("#63873F"), C("#99B95B"), C("#CBDE8A"), C("#A3C86A"), C("#78A24F"), C("#507B3B"), C("#31582E") },
            new float[] { 0, .075f, .21f, .37f, .53f, .69f, .9f, 1 }, false);
        c.ClipRoundRect(left, -cap, width, length + cap * 2, cap);
        // Thin, unequal fibers stay within the rind. A few golden seams are natural
        // variation rather than broad yellow strips or peeled white bamboo faces.
        for (int i = 0; i < 13; i++)
        {
            float fx = left + 1.8f + i * (width - 3.6f) / 12;
            float start = -cap + ((i * 11 + variant * 7) % 17);
            Color fiber = i % 4 == 0 ? A(71, "#E7E8A3") : A(40, "#2F582C");
            c.Bezier(fx, start, fx - .35f, length * .34f, fx + .5f, length * .69f, fx - .2f, length + cap,
                fiber, i % 4 == 0 ? .58f : .36f);
        }
        float goldX = variant % 2 == 0 ? left + 3.2f : width / 2 - 5.3f;
        c.Bezier(goldX, length * .08f, goldX + 1, length * .34f, goldX - .7f, length * .48f, goldX + .3f, length * .62f,
            A(108, "#C9BB56"), 1.8f);
        c.Bezier(left + width * .34f, -cap, left + width * .31f, length * .36f,
            left + width * .35f, length * .71f, left + width * .33f, length + cap, A(96, "#EFF1B3"), .8f);
        c.Pop(); // rind clip
        // Raised circumferential nodes, with dark lower edge and light upper ridge.
        float[] positions = variant < 2 ? new float[] { .31f, .73f } : new float[] { .34f, .74f };
        foreach (float p in positions)
        {
            float node = length * p;
            c.Ellipse(left - .9f, node - 1.8f, width + 1.8f, 5.2f, C("#456E35"));
            c.Bezier(left -.55f, node, left + width * .23f, node + 2.1f, left + width * .75f, node + 2.1f,
                width / 2 + .55f, node, C("#9FB76B"), 1.6f);
            c.Bezier(left + .6f, node - 1.1f, left + width * .27f, node + .3f, left + width * .73f, node + .3f,
                width / 2 - .6f, node - 1.1f, C("#D4D994"), 1.1f);
            c.Line(left + 2, node + 4.9f, left + 2.6f, node + 9.7f, A(115, "#547633"), .85f);
        }
        // The end rims retain green rind and avoid a flat painted-stick appearance.
        c.Ellipse(left + .5f, -cap + .1f, width - 1, 5.1f, C("#92AF5D"));
        c.Bezier(left + 1.7f, -cap + 1.4f, left + width * .28f, -cap + .1f,
            left + width * .73f, -cap + .1f, width / 2 - 1.7f, -cap + 1.4f, A(165, "#D0D991"), .85f);
        c.Bezier(left + 1, length + 2.8f, left + width * .3f, length + cap,
            left + width * .7f, length + cap, width / 2 - 1, length + 2.8f, A(160, "#2A542B"), 1.1f);
        c.Pop(); // stalk transform
    }

    private abstract class Canvas
    {
        public abstract void Push(float x, float y, float rotation);
        public abstract void Pop();
        public abstract void ClipRoundRect(float x, float y, float w, float h, float radius);
        public abstract void RoundRect(float x, float y, float w, float h, float radius, Color color);
        public abstract void GradientRoundRect(float x, float y, float w, float h, float radius, Color[] colors, float[] stops, bool vertical);
        public abstract void Ellipse(float x, float y, float w, float h, Color color);
        public abstract void Bezier(float x1, float y1, float a, float b, float d, float e, float x2, float y2, Color color, float width);
        public abstract void Line(float x1, float y1, float x2, float y2, Color color, float width);
    }

    private static GraphicsPath Rounded(float x, float y, float w, float h, float radius)
    {
        GraphicsPath path = new GraphicsPath(); float diameter = radius * 2;
        path.AddArc(x, y, diameter, diameter, 180, 90); path.AddArc(x + w - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + w - diameter, y + h - diameter, diameter, diameter, 0, 90); path.AddArc(x, y + h - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }

    private sealed class GdiCanvas : Canvas
    {
        private readonly Graphics g;
        private readonly System.Collections.Generic.Stack<GraphicsState> states = new System.Collections.Generic.Stack<GraphicsState>();
        public GdiCanvas(Graphics graphics) { g = graphics; }
        public override void Push(float x, float y, float rotation) { states.Push(g.Save()); g.TranslateTransform(x, y); g.RotateTransform(rotation); }
        public override void Pop() { g.Restore(states.Pop()); }
        public override void ClipRoundRect(float x, float y, float w, float h, float radius)
        { states.Push(g.Save()); using (GraphicsPath path = Rounded(x, y, w, h, radius)) g.SetClip(path, CombineMode.Intersect); }
        public override void RoundRect(float x, float y, float w, float h, float radius, Color color)
        { using (GraphicsPath path = Rounded(x, y, w, h, radius)) using (SolidBrush brush = new SolidBrush(color)) g.FillPath(brush, path); }
        public override void GradientRoundRect(float x, float y, float w, float h, float radius, Color[] colors, float[] stops, bool vertical)
        {
            using (GraphicsPath path = Rounded(x, y, w, h, radius))
            using (LinearGradientBrush brush = new LinearGradientBrush(new PointF(x, y), vertical ? new PointF(x, y + h) : new PointF(x + w, y), colors[0], colors[colors.Length - 1]))
            { brush.InterpolationColors = new ColorBlend { Colors = colors, Positions = stops }; g.FillPath(brush, path); }
        }
        public override void Ellipse(float x, float y, float w, float h, Color color)
        { using (SolidBrush brush = new SolidBrush(color)) g.FillEllipse(brush, x, y, w, h); }
        private static Pen Stroke(Color color, float width)
        { return new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }; }
        public override void Bezier(float x1, float y1, float a, float b, float d, float e, float x2, float y2, Color color, float width)
        { using (Pen pen = Stroke(color, width)) g.DrawBezier(pen, x1, y1, a, b, d, e, x2, y2); }
        public override void Line(float x1, float y1, float x2, float y2, Color color, float width)
        { using (Pen pen = Stroke(color, width)) g.DrawLine(pen, x1, y1, x2, y2); }
    }

    private sealed class SvgCanvas : Canvas
    {
        private readonly StringBuilder body = new StringBuilder();
        private readonly StringBuilder definitions = new StringBuilder();
        private int nextId;
        private static string ColorAttributes(string kind, Color color)
        {
            return kind + "=\"#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2") + "\" "
                + kind + "-opacity=\"" + F(color.A / 255f) + "\"";
        }
        private static string Rect(float x, float y, float w, float h, float radius)
        { return "<rect x=\"" + F(x) + "\" y=\"" + F(y) + "\" width=\"" + F(w) + "\" height=\"" + F(h) + "\" rx=\"" + F(radius) + "\""; }
        public override void Push(float x, float y, float rotation)
        { body.AppendLine("<g transform=\"translate(" + F(x) + " " + F(y) + ") rotate(" + F(rotation) + ")\">"); }
        public override void Pop() { body.AppendLine("</g>"); }
        public override void ClipRoundRect(float x, float y, float w, float h, float radius)
        {
            string id = "clip" + (++nextId);
            definitions.AppendLine("<clipPath id=\"" + id + "\">" + Rect(x, y, w, h, radius) + "/></clipPath>");
            body.AppendLine("<g clip-path=\"url(#" + id + ")\">");
        }
        public override void RoundRect(float x, float y, float w, float h, float radius, Color color)
        { body.AppendLine(Rect(x, y, w, h, radius) + " " + ColorAttributes("fill", color) + "/>"); }
        public override void GradientRoundRect(float x, float y, float w, float h, float radius, Color[] colors, float[] stops, bool vertical)
        {
            string id = "gradient" + (++nextId);
            definitions.AppendLine("<linearGradient id=\"" + id + "\" x1=\"0\" y1=\"0\" x2=\"" + (vertical ? "0" : "1") + "\" y2=\"" + (vertical ? "1" : "0") + "\">");
            for (int i = 0; i < colors.Length; i++)
                definitions.AppendLine("<stop offset=\"" + F(stops[i]) + "\" " + ColorAttributes("stop-color", colors[i]).Replace("stop-color-opacity", "stop-opacity") + "/>");
            definitions.AppendLine("</linearGradient>");
            body.AppendLine(Rect(x, y, w, h, radius) + " fill=\"url(#" + id + ")\"/>");
        }
        public override void Ellipse(float x, float y, float w, float h, Color color)
        { body.AppendLine("<ellipse cx=\"" + F(x + w / 2) + "\" cy=\"" + F(y + h / 2) + "\" rx=\"" + F(w / 2) + "\" ry=\"" + F(h / 2) + "\" " + ColorAttributes("fill", color) + "/>"); }
        public override void Bezier(float x1, float y1, float a, float b, float d, float e, float x2, float y2, Color color, float width)
        { body.AppendLine("<path d=\"M" + F(x1) + " " + F(y1) + "C" + F(a) + " " + F(b) + " " + F(d) + " " + F(e) + " " + F(x2) + " " + F(y2) + "\" fill=\"none\" " + ColorAttributes("stroke", color) + " stroke-width=\"" + F(width) + "\" stroke-linecap=\"round\"/>"); }
        public override void Line(float x1, float y1, float x2, float y2, Color color, float width)
        { body.AppendLine("<path d=\"M" + F(x1) + " " + F(y1) + "L" + F(x2) + " " + F(y2) + "\" fill=\"none\" " + ColorAttributes("stroke", color) + " stroke-width=\"" + F(width) + "\" stroke-linecap=\"round\"/>"); }
        public string Finish()
        { return "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"256\" height=\"256\" viewBox=\"0 0 256 256\">\n<title>Hanqing bamboo M icon</title>\n<defs>\n" + definitions + "</defs>\n" + body + "</svg>\n"; }
    }
}
