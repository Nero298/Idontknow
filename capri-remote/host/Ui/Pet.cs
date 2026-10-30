using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using CapriHost.Core;

namespace CapriHost.Ui
{
    // Một đoạn hoạt ảnh: dải sprite ngang, mỗi khung rộng Strip.Width / Count.
    // Dải ít dùng (glitch, sleep, wake, grin) chỉ nạp khi cần và giải phóng ngay sau đó để tiết kiệm RAM.
    internal sealed class Clip
    {
        internal readonly string Name;
        internal readonly int Count;
        internal readonly double Fps;
        private readonly bool pinned;
        private Bitmap strip;

        internal Clip(string name, int count, double fps, bool pinned)
        {
            Name = name; Count = count; Fps = fps; this.pinned = pinned;
        }

        internal Bitmap Strip
        {
            get
            {
                if (strip == null) strip = Load(Name);
                return strip;
            }
        }

        internal void Release()
        {
            if (!pinned && strip != null) { strip.Dispose(); strip = null; }
        }

        private static Bitmap Load(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Sprite." + name + ".png"))
            {
                if (stream == null) return null;
                using (Image image = Image.FromStream(stream))
                {
                    // Sao chép sang bitmap riêng để không phải giữ luồng tài nguyên mở.
                    Bitmap copy = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(copy)) g.DrawImage(image, 0, 0, image.Width, image.Height);
                    return copy;
                }
            }
        }
    }

    // Capri trên màn hình: cửa sổ trong suốt từng điểm ảnh (UpdateLayeredWindow, chạy được trên Windows 7
    // kể cả khi tắt Aero). Mỗi giây chỉ vẽ lại khi khung hình đổi, hẹn giờ 10 lần/giây nên gần như không tốn CPU.
    internal sealed class PetForm : Form
    {
        private readonly Clip idle = new Clip("idle", 16, 8, true);
        private readonly Clip blink = new Clip("blink", 4, 8, true);
        private readonly Clip blinkGlitch = new Clip("blink_glitch", 4, 8, false);
        private readonly Clip glitch = new Clip("glitch", 10, 15, false);
        private readonly Clip grin = new Clip("grin", 16, 8, false);
        private readonly Clip mouth = new Clip("mouth", 5, 12, true);
        private readonly Clip sleep = new Clip("sleep", 16, 6, false);
        private readonly Clip wake = new Clip("wake", 13, 15, false);

        private readonly Timer timer = new Timer();
        private readonly Random random = new Random();
        private readonly BubbleForm bubble = new BubbleForm();
        private readonly float scale;
        private readonly int frameW;
        private readonly int frameH;
        private readonly Bitmap canvas;

        private Clip clip;
        private bool loop = true;
        private DateTime clipStart = DateTime.UtcNow;
        private Clip drawnClip;
        private int drawnFrame = -1;
        private DateTime nextBlink = DateTime.UtcNow.AddSeconds(3);
        private DateTime nextGlitch = DateTime.UtcNow.AddSeconds(60);
        private DateTime lastActivity = DateTime.UtcNow;
        private DateTime lastNotice = DateTime.MinValue;
        private DateTime talkUntil = DateTime.MinValue;
        private bool talking;
        private bool sleeping;

        internal event Action OpenChat;

        internal PetForm(ContextMenuStrip menu)
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1f, g.DpiX / 96f);
            frameW = (int)Math.Round(150 * scale);
            frameH = (int)Math.Round(240 * scale);
            canvas = new Bitmap(frameW, frameH, PixelFormat.Format32bppArgb);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ClientSize = new Size(frameW, frameH);
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - frameW - 24, area.Bottom - frameH - 8);
            ContextMenuStrip = menu;
            Text = "Capri";

            clip = idle;
            timer.Interval = 100;
            timer.Tick += delegate { Tick(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000;   // WS_EX_LAYERED
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW (không hiện ở Alt+Tab)
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE (bấm vào Capri không cướp focus)
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // ---------------------------------------------------------------- vòng đời

        internal void ShowPet()
        {
            if (!Visible) Show();
            drawnFrame = -1;
            timer.Start();
            Render(0);
        }

        internal void HidePet()
        {
            timer.Stop();
            bubble.Hide();
            Hide();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            bubble.Close();
            canvas.Dispose();
            base.OnFormClosed(e);
        }

        // ---------------------------------------------------------------- Capri nói

        // Capri nói một câu trả lời (chat ngay trên PC, lời chào). Gọi từ luồng giao diện.
        internal void Say(string text)
        {
            lastActivity = DateTime.UtcNow;
            if (!Visible) return;
            WakeUp();
            double seconds = Math.Min(8, 1.5 + text.Length * 0.05);
            talking = true;
            talkUntil = DateTime.UtcNow.AddSeconds(seconds);
            Play(mouth, true);
            bubble.ShowText(text, seconds + 3.5, Bounds);
        }

        // Bong bóng hoạt động: "Nhận lệnh...", "Đang mở YouTube..." rồi tự biến mất.
        internal void Notice(string text, int holdMs)
        {
            lastActivity = DateTime.UtcNow;
            if (!Visible) return;
            WakeUp();
            DateTime now = DateTime.UtcNow;
            // Mỗi lần "bắt" được lệnh mới thì Capri giật nhẹ (glitch) như vừa bắt tín hiệu.
            if (clip == idle && !talking && (now - lastNotice).TotalSeconds > 4) Play(glitch, false);
            lastNotice = now;
            bubble.ShowText(text, holdMs / 1000.0, Bounds);
        }

        private void WakeUp()
        {
            if (!sleeping) return;
            sleeping = false;
            Play(wake, false);
        }

        // ---------------------------------------------------------------- hoạt ảnh

        private void Play(Clip next, bool looping)
        {
            Clip old = clip;
            clip = next;
            loop = looping;
            clipStart = DateTime.UtcNow;
            drawnFrame = -1;
            if (old != next) old.Release();
        }

        private void Settle()
        {
            if (talking) Play(mouth, true);
            else if (sleeping) Play(sleep, true);
            else Play(idle, true);
        }

        private void Tick()
        {
            DateTime now = DateTime.UtcNow;
            if (talking && now > talkUntil) { talking = false; Settle(); }

            double elapsed = (now - clipStart).TotalSeconds;
            int frame = (int)(elapsed * clip.Fps);
            if (!loop && frame >= clip.Count) { Settle(); frame = 0; }
            else frame = frame % clip.Count;

            if (clip == idle && !talking && !sleeping)
            {
                if (now >= nextBlink)
                {
                    nextBlink = now.AddSeconds(2.5 + random.NextDouble() * 3.5);
                    Play(random.Next(4) == 0 ? blinkGlitch : blink, false);
                    frame = 0;
                }
                else if (now >= nextGlitch)
                {
                    nextGlitch = now.AddSeconds(45 + random.NextDouble() * 75);
                    Play(glitch, false);
                    frame = 0;
                }
                else if ((now - lastActivity).TotalMinutes > 10)
                {
                    sleeping = true;
                    Play(sleep, true);
                    frame = 0;
                }
            }

            if (clip != drawnClip || frame != drawnFrame) Render(frame);
        }

        private void Render(int frame)
        {
            if (!IsHandleCreated) return;
            Bitmap strip = clip.Strip;
            if (strip == null) return;
            int sourceW = strip.Width / clip.Count;
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(strip, new Rectangle(0, 0, frameW, frameH),
                    new Rectangle(frame * sourceW, 0, sourceW, strip.Height), GraphicsUnit.Pixel);
            }
            Present();
            drawnClip = clip;
            drawnFrame = frame;
        }

        // Đẩy bitmap có alpha từng điểm ảnh lên cửa sổ layered.
        private void Present()
        {
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memoryDc = Native.CreateCompatibleDC(screenDc);
            IntPtr bitmap = IntPtr.Zero;
            IntPtr previous = IntPtr.Zero;
            try
            {
                bitmap = canvas.GetHbitmap(Color.FromArgb(0));
                previous = Native.SelectObject(memoryDc, bitmap);
                Native.SIZE size = new Native.SIZE();
                size.cx = canvas.Width; size.cy = canvas.Height;
                Native.POINT source = new Native.POINT();
                Native.POINT target = new Native.POINT();
                target.X = Left; target.Y = Top;
                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
                blend.BlendOp = 0; blend.BlendFlags = 0; blend.SourceConstantAlpha = 255; blend.AlphaFormat = 1;
                Native.UpdateLayeredWindow(Handle, screenDc, ref target, ref size, memoryDc, ref source, 0, ref blend, 2);
            }
            finally
            {
                if (bitmap != IntPtr.Zero) { Native.SelectObject(memoryDc, previous); Native.DeleteObject(bitmap); }
                Native.DeleteDC(memoryDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        // ---------------------------------------------------------------- chuột

        protected override void OnMouseDown(MouseEventArgs e)
        {
            lastActivity = DateTime.UtcNow;
            if (e.Button == MouseButtons.Left)
            {
                WakeUp();
                if (e.Clicks >= 2) { Action open = OpenChat; if (open != null) open(); return; }
                if (!talking && clip == idle) Play(grin, false);
                // Kéo cửa sổ bằng thanh tiêu đề giả (HTCAPTION).
                Native.ReleaseCapture();
                Native.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
                bubble.Follow(Bounds);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (IsHandleCreated && Visible) { drawnFrame = -1; }
        }
    }
}
