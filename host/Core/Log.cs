using System;
using System.IO;
using System.Text;

namespace CapriHost.Core
{
    // Nhật ký đơn giản ra %APPDATA%\CapriHost\log.txt (tự cắt khi quá 512 KB).
    internal static class Log
    {
        private static readonly object Gate = new object();

        internal static void Info(string text) { Write("INFO ", text); }

        internal static void Error(string text, Exception error)
        {
            Write("ERROR", text + (error == null ? "" : ": " + error.GetType().Name + " " + error.Message));
        }

        private static void Write(string level, string text)
        {
            try
            {
                lock (Gate)
                {
                    string path = Path.Combine(Settings.DataDir, "log.txt");
                    FileInfo info = new FileInfo(path);
                    if (info.Exists && info.Length > 512 * 1024)
                    {
                        string old = path + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + level + " " + text + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
