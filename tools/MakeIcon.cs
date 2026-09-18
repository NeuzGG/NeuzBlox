using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

// Renders NeuzBlox.ico. BMP entries for the small sizes (widest compatibility),
// a PNG entry for 256 (required, since a raw 256 DIB would be enormous).
static class MakeIcon
{
    static readonly int[] BmpSizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 96 };
    static readonly int[] PngSizes = new int[] { 128, 256 };

    static Bitmap Render(int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            float pad = s * 0.055f;
            var box = new RectangleF(pad, pad, s - pad * 2, s - pad * 2);
            float r = s * 0.235f;

            using (var path = Round(box, r))
            {
                using (var br = new LinearGradientBrush(box, Color.FromArgb(108, 140, 255), Color.FromArgb(155, 108, 255), 35f))
                    g.FillPath(br, path);

                // top gloss
                var half = new RectangleF(box.X, box.Y, box.Width, box.Height * 0.55f);
                using (var gloss = new LinearGradientBrush(half, Color.FromArgb(46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                {
                    Region old = g.Clip;
                    g.SetClip(path, CombineMode.Replace);
                    g.FillRectangle(gloss, half);
                    g.Clip = old;
                }

                using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), Math.Max(1f, s * 0.012f)))
                    g.DrawPath(pen, path);
            }

            // Wordmark "N"
            float fs = s * 0.56f;
            using (var f = new Font("Segoe UI", fs, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                var text = new RectangleF(0, -s * 0.012f, s, s);
                using (var shadow = new SolidBrush(Color.FromArgb(55, 0, 0, 0)))
                    g.DrawString("N", f, shadow, new RectangleF(text.X, text.Y + s * 0.018f, text.Width, text.Height), fmt);
                using (var white = new SolidBrush(Color.White))
                    g.DrawString("N", f, white, text, fmt);
                fmt.Dispose();
            }
        }
        return bmp;
    }

    static GraphicsPath Round(RectangleF b, float r)
    {
        var p = new GraphicsPath();
        float d = r * 2;
        p.AddArc(b.X, b.Y, d, d, 180, 90);
        p.AddArc(b.Right - d, b.Y, d, d, 270, 90);
        p.AddArc(b.Right - d, b.Bottom - d, d, d, 0, 90);
        p.AddArc(b.X, b.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static byte[] AsPng(Bitmap b)
    {
        using (var ms = new MemoryStream())
        {
            b.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    static byte[] AsDib(Bitmap b)
    {
        int w = b.Width, h = b.Height;
        int stride = w * 4;
        int maskStride = ((w + 31) / 32) * 4;
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        bw.Write(40);            // biSize
        bw.Write(w);             // biWidth
        bw.Write(h * 2);         // biHeight (XOR + AND)
        bw.Write((short)1);      // biPlanes
        bw.Write((short)32);     // biBitCount
        bw.Write(0);             // BI_RGB
        bw.Write(stride * h + maskStride * h);
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

        BitmapData bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[stride];
            for (int y = h - 1; y >= 0; y--)
            {
                IntPtr src = (IntPtr)(bd.Scan0.ToInt64() + (long)y * bd.Stride);
                System.Runtime.InteropServices.Marshal.Copy(src, row, 0, stride);
                bw.Write(row, 0, stride);
            }
        }
        finally { b.UnlockBits(bd); }

        var zero = new byte[maskStride];
        for (int y = 0; y < h; y++) bw.Write(zero, 0, maskStride);

        bw.Flush();
        return ms.ToArray();
    }

    static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "NeuzBlox.ico";

        var entries = new List<byte[]>();
        var dims = new List<int>();

        foreach (int s in BmpSizes)
            using (Bitmap b = Render(s)) { entries.Add(AsDib(b)); dims.Add(s); }
        foreach (int s in PngSizes)
            using (Bitmap b = Render(s)) { entries.Add(AsPng(b)); dims.Add(s); }

        using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((short)0);
            bw.Write((short)1);
            bw.Write((short)entries.Count);

            int offset = 6 + 16 * entries.Count;
            for (int i = 0; i < entries.Count; i++)
            {
                int s = dims[i];
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((short)1);
                bw.Write((short)32);
                bw.Write(entries[i].Length);
                bw.Write(offset);
                offset += entries[i].Length;
            }
            foreach (byte[] e in entries) bw.Write(e);
        }

        Console.WriteLine("Wrote " + outPath + " (" + entries.Count + " sizes)");
    }
}
