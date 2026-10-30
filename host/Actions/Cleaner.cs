using System;
using System.IO;
using System.Runtime.InteropServices;
using CapriHost.Core;

namespace CapriHost.Actions
{
    // Dọn dẹp an toàn: chỉ xoá trong thư mục %TEMP% của người dùng và đổ thùng rác.
    internal static class Cleaner
    {
        internal static ToolOutcome Run(string what)
        {
            bool temp = what == "temp" || what == "all";
            bool bin = what == "recycle_bin" || what == "all";
            if (!temp && !bin) return ToolOutcome.Fail("Chỉ dọn được: temp, recycle_bin hoặc all.");

            string report = "";
            if (temp)
            {
                string dir = Path.GetTempPath().TrimEnd('\\', '/');
                string leaf = Path.GetFileName(dir);
                // Chốt an toàn: nếu biến TEMP trỏ vào thư mục lạ thì không đụng vào.
                if (!(string.Equals(leaf, "Temp", StringComparison.OrdinalIgnoreCase) || string.Equals(leaf, "Tmp", StringComparison.OrdinalIgnoreCase)))
                    return ToolOutcome.Fail("Thư mục tạm của bạn (" + dir + ") không có tên Temp nên mình không dám xoá.");
                long bytes = 0; int files = 0, skipped = 0;
                Sweep(dir, DateTime.Now.AddMinutes(-30), 0, ref bytes, ref files, ref skipped);
                report += "Đã xoá " + files + " file tạm (" + Size(bytes) + "), bỏ qua " + skipped + " file đang dùng hoặc mới tạo. ";
            }
            if (bin)
            {
                // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND
                int hr = Native.SHEmptyRecycleBin(IntPtr.Zero, null, 0x7);
                if (hr == 0) report += "Đã đổ thùng rác.";
                else if (hr == unchecked((int)0x8000FFFF) || hr == 1) report += "Thùng rác đã trống sẵn.";
                else report += "Không đổ được thùng rác (mã 0x" + hr.ToString("X") + ").";
            }
            return ToolOutcome.Success(report.Trim());
        }

        private static void Sweep(string dir, DateTime cutoff, int depth, ref long bytes, ref int files, ref int skipped)
        {
            if (depth > 12) return;
            string[] list;
            try { list = Directory.GetFiles(dir); }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
            foreach (string path in list)
            {
                try
                {
                    FileInfo info = new FileInfo(path);
                    if (info.LastWriteTime > cutoff) { skipped++; continue; }
                    long length = info.Length;
                    info.Attributes = FileAttributes.Normal;
                    info.Delete();
                    bytes += length;
                    files++;
                }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
            }
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
            foreach (string sub in subs)
            {
                try
                {
                    if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;   // không đi theo junction/symlink
                    Sweep(sub, cutoff, depth + 1, ref bytes, ref files, ref skipped);
                    Directory.Delete(sub, false);          // chỉ xoá nếu đã rỗng
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static string Size(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024 * 1024)).ToString("0.0") + " GB";
            if (bytes >= 1024L * 1024) return (bytes / (1024.0 * 1024)).ToString("0.0") + " MB";
            return (bytes / 1024.0).ToString("0") + " KB";
        }
    }
}
