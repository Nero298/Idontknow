using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CapriHost.Ai;

namespace CapriHost.Actions
{
    // "Bật nhạc Love Me Not trên YouTube": tìm trên YouTube, lấy video đầu tiên và mở trong trình duyệt.
    // Cách làm: tải trang kết quả tìm kiếm, lấy videoId đầu tiên bằng regex. Đây là cách "tốt nhất có thể",
    // nếu YouTube đổi định dạng trang thì tự rơi về mở trang kết quả tìm kiếm.
    internal static class YouTube
    {
        internal static ToolOutcome Play(string query, string browser, bool openFirst)
        {
            query = (query ?? "").Trim();
            if (query.Length == 0) return ToolOutcome.Fail("Chưa nói tìm bài gì trên YouTube.");

            string results = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query);
            string target = results;
            bool direct = false;
            if (openFirst)
            {
                try
                {
                    Dictionary<string, string> headers = new Dictionary<string, string>();
                    headers["User-Agent"] = "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0 Safari/537.36";
                    headers["Accept-Language"] = "en-US,en;q=0.9";
                    string html = Http.Text("GET", results, null, headers, 9000);
                    Match first = Regex.Match(html, "\"videoId\":\"([A-Za-z0-9_-]{11})\"");
                    if (first.Success)
                    {
                        target = "https://www.youtube.com/watch?v=" + first.Groups[1].Value;
                        direct = true;
                    }
                }
                catch (AiException) { }       // không tải được trang tìm kiếm: mở trang kết quả
            }

            ToolOutcome opened = (browser ?? "").Trim().Length > 0 && !string.Equals(browser, "default", StringComparison.OrdinalIgnoreCase)
                ? AppLauncher.Launch(browser, target)
                : OpenDefault(target);
            if (!opened.Ok) return opened;
            return ToolOutcome.Success(direct
                ? "Đã mở bài đầu tiên tìm được cho \"" + query + "\" trên YouTube."
                : "Đã mở kết quả tìm kiếm \"" + query + "\" trên YouTube.");
        }

        private static ToolOutcome OpenDefault(string url)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo(url);
                info.UseShellExecute = true;
                System.Diagnostics.Process.Start(info);
                return ToolOutcome.Success("ok");
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                return ToolOutcome.Fail("Không mở được trình duyệt: " + error.Message);
            }
        }
    }
}
