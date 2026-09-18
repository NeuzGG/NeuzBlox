using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

// Renders the Discord Rich Presence art asset.
// Full-bleed on purpose: Discord rounds and crops the tile itself, so drawing our
// own rounded corners here would just look inset.
// usage: MakeArt.exe <out.png> [size]
static class MakeArt
{
    static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "neuzblox.png";
        int s = args.Length > 1 ? int.Parse(args[1]) : 1024;

        using (var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                var full = new Rectangle(0, 0, s, s);

                using (var br = new LinearGradientBrush(full,
                    Color.FromArgb(108, 140, 255), Color.FromArgb(155, 108, 255), 35f))
                    g.FillRectangle(br, full);

                // Gloss across the top. Spans the full height with the fade baked into the
                // colour stops - a half-height rect leaves a visible seam at its edge.
                using (var gloss = new LinearGradientBrush(full,
                    Color.FromArgb(52, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                {
                    var blend = new ColorBlend(3);
                    blend.Colors = new Color[]
                    {
                        Color.FromArgb(54, 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255)
                    };
                    blend.Positions = new float[] { 0f, 0.55f, 1f };
                    gloss.InterpolationColors = blend;
                    gloss.WrapMode = WrapMode.TileFlipXY;
                    g.FillRectangle(gloss, full);
                }

                // soft vignette so the mark reads on light Discord themes too
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(-s * 0.25f, -s * 0.25f, s * 1.5f, s * 1.5f);
                    using (var vig = new PathGradientBrush(path))
                    {
                        vig.CenterColor = Color.FromArgb(0, 0, 0, 0);
                        vig.SurroundColors = new Color[] { Color.FromArgb(60, 0, 0, 0) };
                        g.FillRectangle(vig, full);
                    }
                }

                using (var f = new Font("Segoe UI", s * 0.58f, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    var fmt = new StringFormat();
                    fmt.Alignment = StringAlignment.Center;
                    fmt.LineAlignment = StringAlignment.Center;
                    var box = new RectangleF(0, -s * 0.018f, s, s);

                    using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                        g.DrawString("N", f, shadow, new RectangleF(box.X, box.Y + s * 0.02f, box.Width, box.Height), fmt);
                    using (var white = new SolidBrush(Color.White))
                        g.DrawString("N", f, white, box, fmt);
                    fmt.Dispose();
                }
            }

            string dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            bmp.Save(outPath, ImageFormat.Png);
        }

        Console.WriteLine("wrote " + outPath + " (" + s + "x" + s + ")");
    }
}
