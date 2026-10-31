using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriHost.Ui
{
    // Bong bóng thoại nhỏ cạnh Capri. Không cướp focus, tự ẩn sau vài giây.
    internal sealed class BubbleForm : Form
    {
        private static PrivateFontCollection fonts;
        private static IntPtr fontMemory = IntPtr.Zero;
        private readonly Timer hide = new Timer();
        private Font font;
        private string text = "";

        internal BubbleForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.FromArgb(18, 16, 28);
            DoubleBuffered = true;
            font = LoadFont();
            hide.Tick += delegate { hide.Stop(); Hide(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // Phông Share Tech Mono nhúng trong exe; không có thì dùng Consolas.
        private static Font LoadFont()
        {
            try
            {
                if (fonts == null)
                {
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Font.Mono.ttf"))
                    {
                        if (stream != null)
                        {
                            byte[] data = new byte[stream.Length];
                            stream.Read(data, 0, data.Length);
                            fontMemory = Marshal.AllocHGlobal(data.Length);      // phải giữ vùng nhớ này suốt đời tiến trình
                            Marshal.Copy(data, 0, fontMemory, data.Length);
                            fonts = new PrivateFontCollection();
                            fonts.AddMemoryFont(fontMemory, data.Length);
                        }
                    }
                }
                if (fonts != null && fonts.Families.Length > 0) return new Font(fonts.Families[0], 11f, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch (ArgumentException error) { Core.Log.Warn("Giao diện", "Không nạp được phông nhúng, dùng Consolas: " + error.Message, null); }
            catch (ExternalException error) { Core.Log.Warn("Giao diện", "Không nạp được phông nhúng, dùng Consolas: " + error.Message, null); }
            catch (IOException error) { Core.Log.Warn("Giao diện", "Không nạp được phông nhúng, dùng Consolas: " + error.Message, null); }
            return new Font("Consolas", 10.5f);
        }

        internal void ShowText(string message, double seconds, Rectangle anchor)
        {
            text = "> " + message;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int maxWidth = Math.Min(320, area.Width / 3);
            Size size;
            using (Graphics g = CreateGraphics())
            {
                SizeF measured = g.MeasureString(text, font, maxWidth);
                size = new Size((int)Math.Ceiling(measured.Width) + 26, (int)Math.Ceiling(measured.Height) + 20);
            }
            Size = size;
            using (GraphicsPath path = Rounded(new Rectangle(0, 0, size.Width, size.Height), 10))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
            Follow(anchor);
            Invalidate();
            if (!Visible) Show();
            hide.Stop();
            hide.Interval = (int)Math.Max(1500, Math.Min(30000, seconds * 1000));
            hide.Start();
        }

        // Đặt bong bóng bên cạnh Capri (bên trái; hết chỗ thì sang phải).
        internal void Follow(Rectangle anchor)
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int x = anchor.Left - Width + 12;
            if (x < area.Left + 4) x = anchor.Right - 12;
            int y = anchor.Top + anchor.Height / 3 - Height / 2;
            x = Math.Max(area.Left + 4, Math.Min(area.Right - Width - 4, x));
            y = Math.Max(area.Top + 4, Math.Min(area.Bottom - Height - 4, y));
            Location = new Point(x, y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (Pen border = new Pen(Color.FromArgb(255, 42, 109), 2f))
            using (GraphicsPath outline = Rounded(new Rectangle(1, 1, Width - 3, Height - 3), 10))
                e.Graphics.DrawPath(border, outline);
            using (Brush ink = new SolidBrush(Color.FromArgb(5, 217, 232)))
                e.Graphics.DrawString(text, font, ink, new RectangleF(13, 10, Width - 20, Height - 14));
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { hide.Dispose(); if (font != null) font.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
