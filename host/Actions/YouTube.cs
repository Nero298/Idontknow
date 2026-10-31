using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CapriHost.Ai;
using CapriHost.Core;

namespace CapriHost.Actions
{
    // "Bật nhạc Love Me Not trên YouTube": tìm trên YouTube, lấy video đầu tiên và mở trong trình duyệt.
    // Cách làm: tải trang kết quả tìm kiếm, lấy videoId đầu tiên bằng regex. Đây là cách "tốt nhất có thể",
    // nếu YouTube đổi định dạng trang thì tự rơi về mở trang kết quả tìm kiếm.
    internal static class YouTube
    {
        internal static ToolOutcome Play(string query, string browser, bool openFirst, CommandContext cmd)
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
                    headers["User-Agent"] = BrowserLauncher.ChromeUserAgent;
                    headers["Accept-Language"] = "en-US,en;q=0.9";
                    string html = Http.Text("GET", results, null, headers, Timeouts.HttpYouTubeMs, cmd);
                    Match first = Regex.Match(html, "\"videoId\":\"([A-Za-z0-9_-]{11})\"");
                    if (first.Success)
                    {
                        target = "https://www.youtube.com/watch?v=" + first.Groups[1].Value;
                        direct = true;
                    }
                }
                catch (AiException error)
                {
                    // Không tải được trang tìm kiếm: vẫn mở trang kết quả, nhưng ghi lại lý do.
                    Log.Warn("Công cụ", "YouTube: không lấy được video đầu tiên (" + error.Message + ") -> mở trang kết quả", cmd == null ? null : cmd.Id);
                }
            }

            ToolOutcome opened = BrowserLauncher.Open(target, browser);
            if (!opened.Ok) return opened;
            string said = direct
                ? "Đã mở bài đầu tiên tìm được cho \"" + query + "\" trên YouTube."
                : "Đã mở kết quả tìm kiếm \"" + query + "\" trên YouTube.";
            // Giữ nguyên cảnh báo (vd. "Không tìm thấy Chrome...") để điện thoại thấy.
            return ToolOutcome.Success(said + " " + opened.Text);
        }
    }
}
