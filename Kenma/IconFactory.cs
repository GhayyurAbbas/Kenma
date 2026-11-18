using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Windows.Media.Imaging;

namespace Kenma
{
    /// <summary>
    /// Draws the Kenma mark, the "tag gem": two prompt-tag brackets &lt; &gt; that almost close into a diamond,
    /// holding a small polished gem. It stands for what Kenma does: give a prompt structure, then polish it.
    /// Drawn on a 16-unit grid as filled shapes only, so it stays crisp from 16 px up and works in one color.
    /// </summary>
    static class IconFactory
    {
        // Default colors (Windows blue) are used for the exe's file icon and exported logos, which are fixed at build time.
        static Color _accentLight = Color.FromArgb(0x4C, 0xC2, 0xFF);
        static Color _accentDark = Color.FromArgb(0x00, 0x5A, 0x9E);

        /// <summary>Reads the current Windows accent palette (same shades the system uses) and applies it to the mark.</summary>
        public static void UseSystemAccent()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
                if (key?.GetValue("AccentPalette") is byte[] p && p.Length >= 32)
                {
                    // palette: Light3, Light2, Light1, Accent, Dark1, Dark2, Dark3, (unused); 4 bytes RGBA each
                    _accentLight = Color.FromArgb(p[8], p[9], p[10]);
                    _accentDark = Color.FromArgb(p[16], p[17], p[18]);
                    return;
                }
                using var dwm = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (dwm?.GetValue("AccentColor") is int abgr)
                {
                    var c = Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
                    _accentLight = ControlPaint.Light(c, 0.4f);
                    _accentDark = ControlPaint.Dark(c, 0.15f);
                }
            }
            catch { }
        }

        // ---------- geometry ----------

        const float BracketThickness = 2.5f, BracketGap = 1.3f, GemRadius = 2.1f;

        /// <summary>The mark as polygons on a 16 x 16 grid: left bracket, right bracket, centre gem.</summary>
        public static IReadOnlyList<PointF[]> Shapes()
        {
            float d = BracketThickness * 1.4142f, h = BracketGap / 2;
            var left = new[]
            {
                new PointF(8 - h, 0.4f + h), new PointF(0.4f, 8), new PointF(8 - h, 15.6f - h),
                new PointF(8 - h, 15.6f - d - h), new PointF(0.4f + d, 8), new PointF(8 - h, 0.4f + d + h)
            };
            var right = left.Select(p => new PointF(16 - p.X, p.Y)).Reverse().ToArray();
            var gem = new[] { new PointF(8, 8 - GemRadius), new PointF(8 + GemRadius, 8), new PointF(8, 8 + GemRadius), new PointF(8 - GemRadius, 8) };
            return new[] { left, right, gem };
        }

        static Color Lit => Color.FromArgb(Math.Min(255, _accentLight.R + 90), Math.Min(255, _accentLight.G + 70), Math.Min(255, _accentLight.B + 40));

        // ---------- rendering ----------

        public static Bitmap Draw(int s) => Draw(s, onTile: false);

        /// <param name="onTile">White mark on a rounded accent tile instead of the bare accent-colored mark.</param>
        public static Bitmap Draw(int s, bool onTile)
        {
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float u = s / 16f;
            float k = onTile ? 0.66f : 1f; // the mark sits inside the tile's padding

            if (onTile)
            {
                using var tile = Rounded(0.4f * u, 0.4f * u, 15.2f * u, 15.2f * u, 3.6f * u);
                using var bg = new LinearGradientBrush(new RectangleF(0, 0, s, s), _accentLight, _accentDark, 60f);
                g.FillPath(bg, tile);
            }

            using Brush fill = onTile
                ? new SolidBrush(Color.White)
                : new LinearGradientBrush(new RectangleF(0, 0, s, s), Lit, _accentDark, 45f);
            foreach (var shape in Shapes())
                g.FillPolygon(fill, shape.Select(p => new PointF((8 + (p.X - 8) * k) * u, (8 + (p.Y - 8) * k) * u)).ToArray());
            return bmp;
        }

        /// <summary>Renders the mark at several sizes on light and dark backgrounds, bare and on a tile, for design review.</summary>
        public static void SavePreview(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 96, 160 };
            int pad = 18, rowH = 160 + pad * 2, half = pad;
            foreach (var sz in sizes) half += sz + pad;
            var rows = new[] { (false, "Kenma · bare mark"), (true, "Kenma · on a Windows tile") };
            using var sheet = new Bitmap(half * 2, rowH * rows.Length);
            using var g = Graphics.FromImage(sheet);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(243, 243, 243));
            using (var dark = new SolidBrush(Color.FromArgb(32, 32, 32))) g.FillRectangle(dark, half, 0, half, sheet.Height);
            using var font = new Font("Segoe UI", 11f, FontStyle.Bold);
            for (int row = 0; row < rows.Length; row++)
            {
                var (tile, label) = rows[row];
                g.DrawString(label, font, Brushes.DimGray, 6, row * rowH + 2);
                for (int side = 0; side < 2; side++)
                {
                    int x = side * half + pad;
                    foreach (var sz in sizes)
                    {
                        using var icon = Draw(sz, tile);
                        g.DrawImage(icon, x, row * rowH + pad + 160 - sz + 8);
                        x += sz + pad;
                    }
                }
            }
            sheet.Save(path, ImageFormat.Png);
        }

        /// <summary>
        /// Exports the mark as SVG on a 24 x 24 viewBox. <paramref name="mono"/> uses fill="currentColor"
        /// (adapts to light/dark text color); otherwise a diagonal gradient in the current accent colors.
        /// </summary>
        public static void SaveSvg(string path, bool mono)
        {
            const float scale = 1.5f; // 16-unit grid to 24-unit viewBox
            string Pts(PointF[] shape) => string.Join(" ", shape.Select(p =>
                string.Create(CultureInfo.InvariantCulture, $"{p.X * scale:0.###},{p.Y * scale:0.###}")));
            string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

            var sb = new StringBuilder();
            sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" width=\"256\" height=\"256\">");
            sb.AppendLine("  <title>Kenma</title>");
            string fill = "currentColor";
            if (!mono)
            {
                sb.AppendLine("  <defs>");
                sb.AppendLine("    <linearGradient id=\"kenma-gradient\" x1=\"0\" y1=\"0\" x2=\"24\" y2=\"24\" gradientUnits=\"userSpaceOnUse\">");
                sb.AppendLine($"      <stop offset=\"0\" stop-color=\"{Hex(Lit)}\"/>");
                sb.AppendLine($"      <stop offset=\"1\" stop-color=\"{Hex(_accentDark)}\"/>");
                sb.AppendLine("    </linearGradient>");
                sb.AppendLine("  </defs>");
                fill = "url(#kenma-gradient)";
            }
            sb.AppendLine($"  <g fill=\"{fill}\">");
            foreach (var shape in Shapes()) sb.AppendLine($"    <polygon points=\"{Pts(shape)}\"/>");
            sb.AppendLine("  </g>");
            sb.AppendLine("</svg>");
            File.WriteAllText(path, sb.ToString());
        }

        // ---------- icon formats ----------

        static GraphicsPath Rounded(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            float d = Math.Min(r * 2, Math.Min(w, h));
            if (d <= 0.5f) { p.AddRectangle(new RectangleF(x, y, w, h)); return p; }
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static byte[] Png(int size)
        {
            using var b = Draw(size);
            using var ms = new MemoryStream();
            b.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }

        public static byte[] IcoBytes()
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var pngs = new List<byte[]>();
            foreach (var sz in sizes) pngs.Add(Png(sz));
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
            return ms.ToArray();
        }

        public static void SaveIco(string path) => File.WriteAllBytes(path, IcoBytes());

        public static Icon TrayIcon() => new Icon(new MemoryStream(IcoBytes()), 32, 32);

        public static BitmapImage ToWpf(int size)
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = new MemoryStream(Png(size));
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }
    }
}
