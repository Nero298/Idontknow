using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CapriHost.Actions
{
    // Chụp màn hình bằng GDI (CopyFromScreen) - chạy tốt trên Windows 7.
    internal static class ScreenTools
    {
        internal static Rectangle Bounds { get { return Screen.PrimaryScreen.Bounds; } }

        internal static string Info()
        {
            Rectangle b = Bounds;
            return " Their main screen is " + b.Width + "x" + b.Height + " pixels.";
        }

        // Chụp màn hình chính, thu nhỏ còn tối đa maxWidth, nén JPEG. Dùng ít RAM (ảnh 24 bit).
        internal static byte[] Capture(int maxWidth, int quality)
        {
            Rectangle b = Bounds;
            using (Bitmap full = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(full)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
                Bitmap shown = full;
                bool scaled = false;
                if (b.Width > maxWidth)
                {
                    int h = (int)Math.Round(b.Height * (maxWidth / (double)b.Width));
                    shown = new Bitmap(maxWidth, h, PixelFormat.Format24bppRgb);
                    using (Graphics g = Graphics.FromImage(shown))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.DrawImage(full, 0, 0, maxWidth, h);
                    }
                    scaled = true;
                }
                try
                {
                    ImageCodecInfo codec = null;
                    foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders()) if (c.MimeType == "image/jpeg") codec = c;
                    using (MemoryStream ms = new MemoryStream())
                    using (EncoderParameters parameters = new EncoderParameters(1))
                    {
                        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                        shown.Save(ms, codec, parameters);
                        return ms.ToArray();
                    }
                }
                finally { if (scaled) shown.Dispose(); }
            }
        }
    }
}
