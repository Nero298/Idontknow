using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CapriHost.Core
{
    // Một dòng nhật ký (cũng là thứ được đẩy sang chat "Nhật ký lỗi" trên điện thoại).
    internal sealed class LogEntry
    {
        internal long Ts;          // mili giây Unix (UTC)
        internal string Level;     // INFO | WARN | ERROR
        internal string Src;       // AI | Công cụ | Mạng | Lệnh | Trình duyệt | Khởi động ...
        internal string Msg;
        internal string Detail;    // stack trace / thân phản hồi lỗi, cắt còn <= 1,5 KB
        internal string Cmd;       // id lệnh (trùng id tin nhắn trên điện thoại) hoặc rỗng

        internal Dictionary<string, object> ToJson()
        {
            return Json.D("ts", Ts, "level", Level, "src", Src, "msg", Msg, "detail", Detail ?? "", "cmd", Cmd ?? "");
        }
    }

    // Nhật ký: file %APPDATA%\CapriHost\log.txt (tự cắt khi quá 512 KB) + bộ đệm vòng ~300 mục trong RAM.
    internal static class Log
    {
        internal const int Capacity = 300;
        internal const int MaxDetail = 1500;

        private static readonly object Gate = new object();
        private static readonly List<LogEntry> Ring = new List<LogEntry>();
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [ThreadStatic] private static bool pushing;

        // Có dòng mới (Hub gắn vào để đẩy log.push sang điện thoại).
        internal static event Action<LogEntry> Pushed;

        internal static void Info(string text) { Add("INFO", "Host", text, null, null); }
        internal static void Info(string src, string text, string cmd) { Add("INFO", src, text, null, cmd); }
        internal static void Warn(string text) { Add("WARN", "Host", text, null, null); }
        internal static void Warn(string src, string text, string cmd) { Add("WARN", src, text, null, cmd); }

        internal static void Error(string text, Exception error)
        {
            Error("Host", text, error, null);
        }

        internal static void Error(string src, string text, Exception error, string cmd)
        {
            string msg = text + (error == null ? "" : ": " + error.GetType().Name + " " + error.Message);
            Add("ERROR", src, msg, error == null ? null : error.ToString(), cmd);
        }

        // Cửa chung: ghi file + bộ đệm RAM + báo cho người nghe.
        internal static void Add(string level, string src, string msg, string detail, string cmd)
        {
            LogEntry entry = new LogEntry();
            entry.Ts = (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;
            entry.Level = level;
            entry.Src = string.IsNullOrEmpty(src) ? "Host" : src;
            entry.Msg = msg ?? "";
            entry.Detail = Cut(detail, MaxDetail);
            entry.Cmd = cmd ?? "";

            lock (Gate)
            {
                Ring.Add(entry);
                while (Ring.Count > Capacity) Ring.RemoveAt(0);
                WriteFile(entry);
            }

            Action<LogEntry> handler = Pushed;
            if (handler != null && !pushing)
            {
                pushing = true;      // chống đệ quy nếu người nghe lại ghi log
                try { handler(entry); }
                catch (Exception) { /* người nghe lỗi thì bỏ qua: không thể ghi log về lỗi của chính log */ }
                finally { pushing = false; }
            }
        }

        // n mục mới nhất, cũ -> mới.
        internal static List<object> Snapshot(int n)
        {
            List<object> list = new List<object>();
            lock (Gate)
            {
                int start = Math.Max(0, Ring.Count - Math.Max(1, n));
                for (int i = start; i < Ring.Count; i++) list.Add(Ring[i].ToJson());
            }
            return list;
        }

        internal static void Clear()
        {
            lock (Gate) { Ring.Clear(); }
        }

        private static string Cut(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Trim();
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        private static void WriteFile(LogEntry e)
        {
            try
            {
                string path = Path.Combine(Settings.DataDir, "log.txt");
                FileInfo info = new FileInfo(path);
                if (info.Exists && info.Length > 512 * 1024)
                {
                    string old = path + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(path, old);
                }
                StringBuilder line = new StringBuilder();
                line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(' ').Append(e.Level.PadRight(5)).Append(' ').Append(e.Src);
                if (e.Cmd.Length > 0) line.Append(" [").Append(e.Cmd).Append(']');
                line.Append(": ").Append(e.Msg).Append(Environment.NewLine);
                if (e.Detail.Length > 0) line.Append("    ").Append(e.Detail.Replace("\n", "\n    ")).Append(Environment.NewLine);
                File.AppendAllText(path, line.ToString(), Encoding.UTF8);
            }
            catch (IOException) { /* không ghi được file log thì vẫn còn bộ đệm RAM */ }
            catch (UnauthorizedAccessException) { /* như trên */ }
        }
    }
}
