// Deterministic, native vector artwork for the Hanqing listing kit.
// Uses the shared bamboo M renderer. No fonts, app UI, or external images.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public static class HanqingStoreArtwork
{
    private static Color C(string hex) { return ColorTranslator.FromHtml(hex); }
    private static GraphicsPath RoundRect(float x, float y, float w, float h, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(x, y, d, d, 180, 90); path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90); path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }

    // Palette and the long fibers/node joints match src/MarkPad/Theming/BambooChrome.cs.
    private static void BambooStrip(Graphics g, float x, float y, float width, float length, bool horizontal)
    {
        GraphicsState state = g.Save();
        g.TranslateTransform(x, y);
        if (horizontal) { g.TranslateTransform(length, 0); g.RotateTransform(90); }
        int count = 2; float slatWidth = width / count;
        try
        {
            using (SolidBrush baseBrush = new SolidBrush(C("#AE9567"))) g.FillRectangle(baseBrush, 0, 0, width, length);
            for (int slat = 0; slat < count; slat++)
            {
                float left = slat * slatWidth;
                using (LinearGradientBrush face = new LinearGradientBrush(new PointF(left, 0), new PointF(left + slatWidth, 0), C("#C5AD7C"), C("#BFA577")))
                {
                    face.InterpolationColors = new ColorBlend
                    {
                        Colors = new Color[] { C("#C5AD7C"), C("#E1D0A9"), C("#EBDFBD"), C("#E4D5AF"), C("#D8C397"), C("#BFA577") },
                        Positions = new float[] { 0, .09f, .33f, .64f, .94f, 1 }
                    };
                    g.FillRectangle(face, left + .65f, 0, slatWidth - 1.3f, length);
                }
                for (int fiber = 0; fiber < 13; fiber++)
                {
                    float fx = left + 2 + fiber * (slatWidth - 4) / 13;
                    using (Pen pen = new Pen(fiber % 3 == 0 ? Color.FromArgb(40, 255, 243, 192) : Color.FromArgb(24, 108, 80, 20), fiber % 4 == 0 ? .8f : .45f))
                        g.DrawBezier(pen, fx, 0, fx - .8f, length * .3f, fx + .9f, length * .72f, fx - .2f, length);
                }
                for (float joint = slat == 0 ? 110 : 214; joint < length; joint += 310)
                {
                    using (Pen node = new Pen(Color.FromArgb(52, 116, 90, 42), 1.1f))
                        g.DrawBezier(node, left + 1, joint, left + 11, joint + 1.8f, left + slatWidth - 10, joint - 1.2f, left + slatWidth - 1, joint + .5f);
                    using (Pen highlight = new Pen(Color.FromArgb(92, 255, 247, 211), .8f))
                        g.DrawLine(highlight, left + 2, joint + 2, left + slatWidth - 2, joint + 2.4f);
                }
            }
        }
        finally { g.Restore(state); }
    }

    private static void Paper(Graphics g, int w, int h)
    {
        g.Clear(C("#F3F0E7")); // Same light-paper base as the product's PaperTexture.
        Random random = new Random(20260928);
        for (int i = 0; i < w * h / 120; i++)
        {
            float x = (float)random.NextDouble() * w, y = (float)random.NextDouble() * h;
            float length = 2 + (float)random.NextDouble() * 13;
            Color color = i % 3 == 0 ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(7, 120, 104, 73);
            using (Pen pen = new Pen(color, .5f)) g.DrawLine(pen, x, y, x + length, y + (float)random.NextDouble() * 2 - 1);
        }
        float border = Math.Min(w, h) * .052f;
        BambooStrip(g, 0, 0, border, h, false);
        BambooStrip(g, w - border, 0, border, h, false);
        BambooStrip(g, 0, 0, border, w, true);
        BambooStrip(g, 0, h - border, border, w, true);
        using (Pen inner = new Pen(Color.FromArgb(95, 95, 79, 44), 1.6f))
            g.DrawRectangle(inner, border + 3, border + 3, w - border * 2 - 6, h - border * 2 - 6);
        using (Pen light = new Pen(Color.FromArgb(140, 255, 255, 239), 1))
            g.DrawRectangle(light, border + 6, border + 6, w - border * 2 - 12, h - border * 2 - 12);
    }

    private static void Leaf(Graphics g, float x, float y, float length, float angle, int alpha)
    {
        GraphicsState state = g.Save(); g.TranslateTransform(x, y); g.RotateTransform(angle);
        using (GraphicsPath path = new GraphicsPath())
        using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, 55, 79, 53)))
        {
            path.AddBezier(0, 0, length * .35f, -length * .22f, length * .75f, -length * .07f, length, 0);
            path.AddBezier(length, 0, length * .63f, length * .08f, length * .22f, length * .12f, 0, 0);
            g.FillPath(brush, path);
        }
        g.Restore(state);
    }

    private static void Branch(Graphics g, float x, float y, float scale, bool mirror)
    {
        GraphicsState state = g.Save(); g.TranslateTransform(x, y); g.ScaleTransform(mirror ? -scale : scale, scale);
        using (Pen stem = new Pen(Color.FromArgb(160, 63, 85, 54), 3))
        {
            g.DrawBezier(stem, 0, 0, 36, -170, 140, -250, 220, -375);
            g.DrawBezier(stem, 59, -159, 16, -232, 10, -288, -15, -340);
            g.DrawBezier(stem, 121, -247, 170, -226, 208, -215, 254, -223);
        }
        Leaf(g, 23, -92, 103, -30, 180); Leaf(g, 29, -110, 81, -142, 170);
        Leaf(g, 64, -166, 124, -18, 205); Leaf(g, 79, -189, 99, -106, 184);
        Leaf(g, 125, -251, 110, -15, 165); Leaf(g, 140, -274, 89, -120, 210);
        Leaf(g, 182, -322, 106, -47, 210); Leaf(g, 192, -335, 67, -101, 145);
        Leaf(g, 25, -247, 83, -151, 180); Leaf(g, 11, -283, 90, -95, 220);
        Leaf(g, 178, -229, 74, 34, 190); Leaf(g, 218, -223, 80, -23, 210);
        g.Restore(state);
    }

    private static void Sheet(Graphics g, float x, float y, float w, float h, float rotation)
    {
        GraphicsState state = g.Save(); g.TranslateTransform(x, y); g.RotateTransform(rotation);
        using (SolidBrush shadow = new SolidBrush(Color.FromArgb(12, 52, 49, 33)))
            for (int i = 10; i >= 1; i--) g.FillRectangle(shadow, -w / 2 + i * .8f, -h / 2 + i * 1.2f, w, h);
        using (SolidBrush face = new SolidBrush(C("#FFFCF1"))) g.FillRectangle(face, -w / 2, -h / 2, w, h);
        using (Pen edge = new Pen(C("#D5CBB5"), 1.2f)) g.DrawRectangle(edge, -w / 2, -h / 2, w, h);
        g.Restore(state);
    }

    private static void BookAndBrush(Graphics g, float x, float y, float scale)
    {
        GraphicsState state = g.Save(); g.TranslateTransform(x, y); g.ScaleTransform(scale, scale); g.RotateTransform(-6);
        // Open, blank folded sheets: symbolic artwork, not a simulated product window.
        using (SolidBrush shadow = new SolidBrush(Color.FromArgb(20, 51, 55, 41)))
            g.FillEllipse(shadow, -345, 188, 720, 55);
        PointF[] left = { new PointF(-318, -200), new PointF(-22, -172), new PointF(0, -146), new PointF(0, 228), new PointF(-34, 207), new PointF(-318, 178) };
        PointF[] right = { new PointF(0, -146), new PointF(34, -176), new PointF(318, -202), new PointF(318, 178), new PointF(34, 205), new PointF(0, 228) };
        using (SolidBrush cover = new SolidBrush(C("#49684D")))
        { g.FillPolygon(cover, new PointF[] { new PointF(-332, -197), new PointF(0, -140), new PointF(330, -197), new PointF(330, 196), new PointF(0, 248), new PointF(-332, 197) }); }
        using (SolidBrush paper = new SolidBrush(C("#FFFDF5"))) { g.FillPolygon(paper, left); g.FillPolygon(paper, right); }
        using (Pen edge = new Pen(C("#D8D1BE"), 1.4f)) { g.DrawPolygon(edge, left); g.DrawPolygon(edge, right); }
        using (Pen crease = new Pen(C("#C7C9B3"), 3)) g.DrawLine(crease, 0, -139, 0, 225);
        for (int i = 0; i < 4; i++)
        {
            using (Pen layer = new Pen(Color.FromArgb(100, 244, 237, 217), 1))
            { g.DrawLine(layer, -318, 184 + i * 3, -34, 213 + i * 3); g.DrawLine(layer, 34, 211 + i * 3, 318, 184 + i * 3); }
        }
        // A single non-letter ink stroke and bamboo brush express writing, without text.
        using (GraphicsPath stroke = new GraphicsPath())
        using (SolidBrush ink = new SolidBrush(C("#3B5143")))
        {
            stroke.AddBezier(-229, 78, -94, 15, 58, 107, 244, -72);
            stroke.AddBezier(244, -72, 151, 128, -76, 55, -229, 78);
            g.FillPath(ink, stroke);
        }
        GraphicsState brushState = g.Save(); g.TranslateTransform(178, -14); g.RotateTransform(29);
        using (GraphicsPath handle = RoundRect(-14, -290, 28, 276, 10))
        using (LinearGradientBrush bamboo = new LinearGradientBrush(new PointF(-14, 0), new PointF(14, 0), C("#967441"), C("#E6D6AC")))
            g.FillPath(bamboo, handle);
        using (SolidBrush band = new SolidBrush(C("#43533D"))) { g.FillRectangle(band, -15, -60, 30, 13); g.FillRectangle(band, -15, -22, 30, 13); }
        using (GraphicsPath nib = new GraphicsPath())
        using (SolidBrush hair = new SolidBrush(C("#243C30")))
        { nib.AddBezier(-14, -8, -13, 26, -2, 45, 0, 61); nib.AddBezier(0, 61, 14, 25, 16, 7, 14, -8); nib.CloseFigure(); g.FillPath(hair, nib); }
        g.Restore(brushState); g.Restore(state);
    }

    public static void Render(string outputPath, int width, int height, string kind)
    {

        int factor = kind == "icon" ? 4 : 2;
        using (Bitmap large = new Bitmap(width * factor, height * factor, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(large))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.ScaleTransform(factor, factor);
            if (kind == "icon") { g.Clear(Color.Transparent); HanqingIconRenderer.Draw(g, 0, 0, width); }
            else
            {
                Paper(g, width, height);
                float min = Math.Min(width, height), scale = min / 1080;
                if (kind == "hero")
                {
                    Branch(g, width * .15f, height * .79f, 1.26f, false);
                    Branch(g, width * .875f, height * .62f, .98f, true);
                    BookAndBrush(g, width * .515f, height * .45f, 1.4f);
                }
                else
                {
                    Branch(g, width * .13f, height * .81f, scale * 1.15f, false);
                    Branch(g, width * .865f, height * .53f, scale * .83f, true);
                    float centerY = height * .405f, sheetW = min * .56f, sheetH = min * .65f;
                    Sheet(g, width * .5f, centerY + min * .02f, sheetW, sheetH, -9);
                    Sheet(g, width * .5f, centerY, sheetW, sheetH, 5);
                    float iconSize = min * .37f;
                    HanqingIconRenderer.Draw(g, (width - iconSize) / 2, centerY - iconSize / 2, iconSize);
                }
            }
            using (Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics output = Graphics.FromImage(result))
            {
                // Keep promotion canvases opaque even at resampled outer edges.
                // The separate Store icon deliberately retains its transparent corners.
                output.Clear(kind == "icon" ? Color.Transparent : C("#DCCBA4"));
                output.CompositingMode = kind == "icon" ? CompositingMode.SourceCopy : CompositingMode.SourceOver;
                output.InterpolationMode = InterpolationMode.HighQualityBicubic;
                output.PixelOffsetMode = PixelOffsetMode.HighQuality;
                output.DrawImage(large, new Rectangle(0, 0, width, height));
                result.SetResolution(96, 96); result.Save(outputPath, ImageFormat.Png);
            }
        }
    }
}
