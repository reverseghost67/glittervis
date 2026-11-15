using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace GlitterVis;

/// <summary>Album-art colors and the title card picture.</summary>
public static class Art
{
    /// <summary>Four colors (RGB 0..1) picked from a cover: the most common saturated colors, darkest first.</summary>
    public static float[][] Palette(string coverPath)
    {
        try
        {
            using var src = new Bitmap(coverPath);
            using var small = new Bitmap(src, new Size(48, 48));
            var buckets = new Dictionary<int, (float r, float g, float b, int n, float sat)>();
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 48; x++)
                {
                    var c = small.GetPixel(x, y);
                    float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
                    float mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                    float sat = mx <= 0 ? 0 : (mx - mn) / mx;
                    if (mx < 0.08f) continue;                                   // skip near-black
                    int key = (c.R >> 5) << 6 | (c.G >> 5) << 3 | (c.B >> 5);    // 8x8x8 color cube
                    buckets.TryGetValue(key, out var a);
                    buckets[key] = (a.r + r, a.g + g, a.b + b, a.n + 1, a.sat + sat);
                }
            var picks = buckets.Values
                .Select(v => (r: v.r / v.n, g: v.g / v.n, b: v.b / v.n, score: v.n * (0.35f + v.sat / v.n)))
                .OrderByDescending(v => v.score).Take(12).ToList();
            var chosen = new List<float[]>();
            foreach (var p in picks)
            {
                var c = new[] { p.r, p.g, p.b };
                if (chosen.All(o => Dist(o, c) > 0.18f)) chosen.Add(c);
                if (chosen.Count == 4) break;
            }
            if (chosen.Count < 2) return null;
            while (chosen.Count < 4) chosen.Add(new[] { Math.Min(1, chosen[^1][0] * 1.4f + 0.1f), Math.Min(1, chosen[^1][1] * 1.4f + 0.1f), Math.Min(1, chosen[^1][2] * 1.4f + 0.1f) });
            return chosen.OrderBy(c => c[0] * 0.3f + c[1] * 0.59f + c[2] * 0.11f).ToArray();
        }
        catch (Exception e) { Log.Write("palette failed: " + e.Message); return null; }
    }

    static float Dist(float[] a, float[] b) => MathF.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));

    /// <summary>A transparent RGBA picture with the artist, title and (if found) a small cover, for the title card.</summary>
    public static (byte[] rgba, int w, int h) TitleCard(string artist, string title, string coverPath)
    {
        int W = 1400, H = 260;
        using var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int x = 20;
            if (coverPath != null)
            {
                try
                {
                    using var cov = new Bitmap(coverPath);
                    using var path = RoundRect(new Rectangle(20, 20, 220, 220), 18);
                    using var shadow = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
                    g.FillPath(shadow, RoundRect(new Rectangle(26, 28, 220, 220), 18));
                    g.SetClip(path);
                    g.DrawImage(cov, new Rectangle(20, 20, 220, 220));
                    g.ResetClip();
                    x = 270;
                }
                catch { }
            }
            using var big = new Font("Segoe UI Light", 64, FontStyle.Regular, GraphicsUnit.Pixel);
            using var small = new Font("Segoe UI Semibold", 34, FontStyle.Regular, GraphicsUnit.Pixel);
            using var shade = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
            using var white = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
            using var soft = new SolidBrush(Color.FromArgb(200, 230, 230, 235));
            var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            var r1 = new RectangleF(x, 60, W - x - 20, 90);
            var r2 = new RectangleF(x, 150, W - x - 20, 50);
            g.DrawString(title, big, shade, new RectangleF(r1.X + 3, r1.Y + 3, r1.Width, r1.Height), fmt);
            g.DrawString(title, big, white, r1, fmt);
            if (!string.IsNullOrEmpty(artist))
            {
                g.DrawString(artist.ToUpperInvariant(), small, shade, new RectangleF(r2.X + 2, r2.Y + 2, r2.Width, r2.Height), fmt);
                g.DrawString(artist.ToUpperInvariant(), small, soft, r2, fmt);
            }
        }
        var data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bytes = new byte[W * H * 4];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        bmp.UnlockBits(data);
        for (int i = 0; i < bytes.Length; i += 4) (bytes[i], bytes[i + 2]) = (bytes[i + 2], bytes[i]);   // BGRA -> RGBA
        return (bytes, W, H);
    }

    static GraphicsPath RoundRect(Rectangle r, int rad)
    {
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
        p.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
        p.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
        p.AddArc(r.X, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
        p.CloseFigure();
        return p;
    }
}

public static class Log
{
    static readonly string path = Path.Combine(AppContext.BaseDirectory, "glittervis.log");
    public static void Write(string m) { try { File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss ") + m + Environment.NewLine); } catch { } }
}
