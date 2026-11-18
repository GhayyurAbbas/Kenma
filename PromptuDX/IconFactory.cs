using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using System.Windows.Media.Imaging;

namespace PromptuDX
{
    /// <summary>Draws the blocky mascot on a badge tinted with the Windows accent color.</summary>
    static class IconFactory
    {
        // Default badge (Windows blue) is used for the exe's file icon, which is fixed at build time.
        static Color _badgeLight = Color.FromArgb(0x4C, 0xC2, 0xFF);
        static Color _badgeDark = Color.FromArgb(0x00, 0x5A, 0x9E);

        /// <summary>Reads the current Windows accent palette (same shades the system uses) and applies it to the badge.</summary>
        public static void UseSystemAccent()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
                if (key?.GetValue("AccentPalette") is byte[] p && p.Length >= 32)
                {
                    // palette: Light3, Light2, Light1, Accent, Dark1, Dark2, Dark3, (unused); 4 bytes RGBA each
                    _badgeLight = Color.FromArgb(p[8], p[9], p[10]);
                    _badgeDark = Color.FromArgb(p[16], p[17], p[18]);
                    return;
                }
                using var dwm = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (dwm?.GetValue("AccentColor") is int abgr)
                {
                    var c = Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
                    _badgeLight = ControlPaint.Light(c, 0.4f);
                    _badgeDark = ControlPaint.Dark(c, 0.15f);
                }
            }
            catch { }
        }

        public static Bitmap Draw(int s)
        {
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float u = s / 16f;

            using var clip = new GraphicsPath();
            clip.AddEllipse(0, 0, s - 1, s - 1);
            using (var bg = new LinearGradientBrush(new RectangleF(0, 0, s, s), _badgeLight, _badgeDark, 45f))
                g.FillPath(bg, clip);
            g.SetClip(clip);

            void Block(string hex, float x, float y, float w, float h, float r = 0.35f)
            {
                using var b = new SolidBrush(ColorTranslator.FromHtml(hex));
                using var p = Rounded(x * u, y * u, w * u, h * u, r * u);
                g.FillPath(b, p);
            }

            Block("#0D69AC", 4.9f, 6.3f, 6.2f, 4.6f);          // torso
            Block("#F5CD30", 3.2f, 6.3f, 1.6f, 4.4f);          // left arm
            Block("#F5CD30", 11.2f, 6.3f, 1.6f, 4.4f);         // right arm
            Block("#4B974B", 4.9f, 11.0f, 3.0f, 4.2f);         // left leg
            Block("#4B974B", 8.1f, 11.0f, 3.0f, 4.2f);         // right leg
            Block("#F5CD30", 7.1f, 5.6f, 1.8f, 0.9f, 0.1f);    // neck
            Block("#F5CD30", 5.4f, 1.6f, 5.2f, 4.5f, 0.9f);    // head

            // face
            Block("#1B1B1B", 6.6f, 2.9f, 0.7f, 1.0f, 0.3f);
            Block("#1B1B1B", 8.7f, 2.9f, 0.7f, 1.0f, 0.3f);
            if (s >= 24)
            {
                using var pen = new Pen(Color.FromArgb(27, 27, 27), System.Math.Max(1f, 0.35f * u)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawArc(pen, 6.7f * u, 3.3f * u, 2.6f * u, 1.6f * u, 20, 140);
            }
            // anime blush
            if (s >= 32)
            {
                using var blush = new SolidBrush(Color.FromArgb(120, 255, 105, 160));
                g.FillEllipse(blush, 5.8f * u, 4.1f * u, 0.9f * u, 0.5f * u);
                g.FillEllipse(blush, 9.3f * u, 4.1f * u, 0.9f * u, 0.5f * u);
            }
            return bmp;
        }

        static GraphicsPath Rounded(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            float d = System.Math.Min(r * 2, System.Math.Min(w, h));
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
