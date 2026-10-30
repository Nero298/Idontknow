using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using CapriHost.Ai;
using CapriHost.Core;

namespace CapriHost.Actions
{
    // Danh sách "tay chân" của Capri. Vừa là danh mục công cụ AI được phép gọi (Specs),
    // vừa là nơi thực thi (Run). Nút nhanh trên điện thoại cũng đi qua đây, không cần AI.
    internal static class Tools
    {
        private static List<ToolSpec> specs;

        internal static List<ToolSpec> Specs()
        {
            if (specs != null) return specs;
            List<ToolSpec> s = new List<ToolSpec>();
            s.Add(new ToolSpec("open_website", "Open a website or run a web search in the default browser.",
                new ToolParam("url", "string", "A full URL, a domain like youtube.com, or plain search words.", true)));
            s.Add(new ToolSpec("play_youtube", "Search YouTube for a song or video and open the top result in the browser so it starts playing. Use this when the user asks to play music or a video.",
                new ToolParam("query", "string", "What to play, for example the song and artist.", true),
                new ToolParam("browser", "string", "Browser to use, for example chrome or firefox. Leave empty for the default browser.", false)));
            s.Add(new ToolSpec("open_app", "Launch an application on the PC.",
                new ToolParam("name", "string", "App name, for example: chrome, firefox, edge, discord, spotify, notepad, calculator, explorer, paint, taskmgr, word, excel, powerpoint, vscode, steam, zalo, control.", true),
                new ToolParam("args", "string", "Optional arguments, for example a folder path for explorer.", false)));
            s.Add(new ToolSpec("close_app", "Close a running application politely (like clicking X). Use force only if it will not close.",
                new ToolParam("name", "string", "App name or part of the window title.", true),
                new ToolParam("force", "boolean", "Kill the process. Needs the user's permission.", false)));
            s.Add(new ToolSpec("list_windows", "List the open windows on the PC."));
            s.Add(new ToolSpec("get_active_window", "Tell which window is currently in front."));
            s.Add(new ToolSpec("cleanup", "Clean the PC: delete temporary files and/or empty the recycle bin. Asks the user to confirm.",
                new ToolParam("what", "string", "What to clean.", true, "temp", "recycle_bin", "all")));
            s.Add(new ToolSpec("press_keys", "Press a keyboard shortcut or key, for example ctrl+c, alt+f4, win+d, enter, f5.",
                new ToolParam("keys", "string", "Keys joined with +.", true)));
            s.Add(new ToolSpec("type_text", "Type text into the window that has focus.",
                new ToolParam("text", "string", "The text to type.", true),
                new ToolParam("enter", "boolean", "Press Enter afterwards.", false)));
            s.Add(new ToolSpec("mouse_click", "Click the mouse at screen pixel coordinates.",
                new ToolParam("x", "integer", "X pixel from the left edge of the main screen.", true),
                new ToolParam("y", "integer", "Y pixel from the top edge of the main screen.", true),
                new ToolParam("button", "string", "Which button.", false, "left", "right", "middle"),
                new ToolParam("double", "boolean", "Double click.", false)));
            s.Add(new ToolSpec("mouse_move", "Move the mouse pointer to screen pixel coordinates.",
                new ToolParam("x", "integer", "X pixel.", true),
                new ToolParam("y", "integer", "Y pixel.", true)));
            s.Add(new ToolSpec("scroll", "Scroll the mouse wheel.",
                new ToolParam("amount", "integer", "Positive scrolls up, negative scrolls down. Each unit is one wheel notch.", true)));
            s.Add(new ToolSpec("screenshot", "Take a screenshot of the PC screen and send it to the user's phone."));
            s.Add(new ToolSpec("look_at_screen", "Take a screenshot and look at it to answer a question about what is on screen.",
                new ToolParam("question", "string", "What to find out.", true)));
            s.Add(new ToolSpec("media", "Control music and video playback.",
                new ToolParam("action", "string", "Playback action.", true, "play_pause", "next", "previous", "stop")));
            s.Add(new ToolSpec("volume", "Change the PC volume.",
                new ToolParam("action", "string", "Volume action.", true, "volume_up", "volume_down", "mute"),
                new ToolParam("steps", "integer", "How many steps (each is about 2 percent). Default 5.", false)));
            s.Add(new ToolSpec("lock_screen", "Lock the Windows session."));
            specs = s;
            return s;
        }

        internal static string Shorten(string text, int max)
        {
            text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }

        // Câu mô tả ngắn hiện trong bong bóng cạnh Capri khi một công cụ bắt đầu chạy.
        internal static string Describe(string name, Dictionary<string, object> a)
        {
            if (a == null) a = new Dictionary<string, object>();
            switch (name)
            {
                case "open_website": return "Đang mở " + Shorten(Json.Str(a, "url", ""), 40);
                case "play_youtube": return "Đang mở YouTube: " + Shorten(Json.Str(a, "query", ""), 34);
                case "open_app": return "Đang mở " + Shorten(Json.Str(a, "name", "ứng dụng"), 30);
                case "close_app": return "Đang đóng " + Shorten(Json.Str(a, "name", "ứng dụng"), 30);
                case "list_windows": return "Đang xem các cửa sổ đang mở";
                case "get_active_window": return "Đang xem cửa sổ hiện tại";
                case "cleanup": return "Đang dọn dẹp máy";
                case "press_keys": return "Đang bấm " + Shorten(Json.Str(a, "keys", "phím"), 24);
                case "type_text": return "Đang gõ chữ";
                case "mouse_click": return "Đang bấm chuột";
                case "mouse_move": return "Đang di chuột";
                case "scroll": return "Đang cuộn trang";
                case "screenshot": return "Đang chụp màn hình";
                case "look_at_screen": return "Đang nhìn màn hình";
                case "media": return "Đang chỉnh nhạc";
                case "volume": return "Đang chỉnh âm lượng";
                case "lock_screen": return "Đang khoá máy";
            }
            return "Đang làm: " + name;
        }

        // Hỏi xác nhận trước khi làm việc có thể mất dữ liệu.
        internal static bool Allowed(ToolContext ctx, string question)
        {
            if (ctx == null) return false;
            if (ctx.UserInitiated) return true;
            if (ctx.Confirm == null) return false;
            try { return ctx.Confirm(question); }
            catch (Exception) { return false; }
        }

        internal static ToolOutcome Run(string name, Dictionary<string, object> args, ToolContext ctx)
        {
            if (args == null) args = new Dictionary<string, object>();
            try
            {
                switch (name)
                {
                    case "open_website": return OpenWebsite(Json.Str(args, "url", ""));
                    case "play_youtube": return YouTube.Play(Json.Str(args, "query", ""), Json.Str(args, "browser", ""), true);
                    case "open_app": return AppLauncher.Launch(Json.Str(args, "name", ""), Json.Str(args, "args", ""));
                    case "close_app": return AppLauncher.Close(Json.Str(args, "name", ""), Json.Bool(args, "force", false), ctx);
                    case "list_windows": return ToolOutcome.Success(AppLauncher.ListWindows());
                    case "get_active_window": return ToolOutcome.Success(AppLauncher.ActiveWindow());
                    case "cleanup":
                    {
                        string what = Json.Str(args, "what", "temp");
                        if (!Allowed(ctx, "Dọn dẹp máy (" + what + ")? File tạm và thùng rác sẽ bị xoá.")) return ToolOutcome.Fail("Người dùng không cho phép dọn dẹp.");
                        return Cleaner.Run(what);
                    }
                    case "press_keys":
                    {
                        if (!InputAllowed()) return Off();
                        string problem = InputControl.Press(Json.Str(args, "keys", ""));
                        return problem == null ? ToolOutcome.Success("Đã bấm " + Json.Str(args, "keys", "") + ".") : ToolOutcome.Fail(problem);
                    }
                    case "type_text":
                    {
                        if (!InputAllowed()) return Off();
                        string text = Json.Str(args, "text", "");
                        if (text.Length > 2000) return ToolOutcome.Fail("Văn bản quá dài (tối đa 2000 ký tự).");
                        InputControl.TypeText(text, Json.Bool(args, "enter", false));
                        return ToolOutcome.Success("Đã gõ " + text.Length + " ký tự.");
                    }
                    case "mouse_click":
                    {
                        if (!InputAllowed()) return Off();
                        int x = Json.Int(args, "x", -1), y = Json.Int(args, "y", -1);
                        if (!OnScreen(x, y)) return ToolOutcome.Fail("Toạ độ ngoài màn hình.");
                        InputControl.Click(x, y, Json.Str(args, "button", "left"), Json.Bool(args, "double", false));
                        return ToolOutcome.Success("Đã bấm chuột tại " + x + "," + y + ".");
                    }
                    case "mouse_move":
                    {
                        if (!InputAllowed()) return Off();
                        int x = Json.Int(args, "x", -1), y = Json.Int(args, "y", -1);
                        if (!OnScreen(x, y)) return ToolOutcome.Fail("Toạ độ ngoài màn hình.");
                        InputControl.MoveTo(x, y);
                        return ToolOutcome.Success("Đã di chuột tới " + x + "," + y + ".");
                    }
                    case "scroll":
                    {
                        if (!InputAllowed()) return Off();
                        InputControl.Scroll(Math.Max(-50, Math.Min(50, Json.Int(args, "amount", 0))));
                        return ToolOutcome.Success("Đã cuộn.");
                    }
                    case "screenshot":
                    {
                        ToolOutcome shot = ToolOutcome.Success("Đã chụp màn hình và gửi cho người dùng.");
                        shot.Image = ScreenTools.Capture(1280, 70);
                        return shot;
                    }
                    case "look_at_screen":
                    {
                        byte[] jpeg = ScreenTools.Capture(1024, 60);
                        string seen = Agent.Gemini.Describe(jpeg, Json.Str(args, "question", "Describe what is on the screen."));
                        return ToolOutcome.Success(seen);
                    }
                    case "media":
                        return InputControl.MediaKey(Json.Str(args, "action", "")) ? ToolOutcome.Success("Đã gửi lệnh nhạc.") : ToolOutcome.Fail("Lệnh nhạc không hợp lệ.");
                    case "volume":
                    {
                        string action = Json.Str(args, "action", "");
                        int steps = action == "mute" ? 1 : Math.Max(1, Math.Min(25, Json.Int(args, "steps", 5)));
                        for (int i = 0; i < steps; i++) if (!InputControl.MediaKey(action)) return ToolOutcome.Fail("Lệnh âm lượng không hợp lệ.");
                        return ToolOutcome.Success("Đã chỉnh âm lượng.");
                    }
                    case "lock_screen":
                        return Native.LockWorkStation() ? ToolOutcome.Success("Đã khoá máy.") : ToolOutcome.Fail("Không khoá được máy.");
                }
                return ToolOutcome.Fail("Không có công cụ tên '" + name + "'.");
            }
            catch (AiException error) { return ToolOutcome.Fail(error.Message); }
            catch (Win32Exception error) { return ToolOutcome.Fail("Windows báo lỗi: " + error.Message); }
            catch (Exception error)
            {
                Log.Error("Công cụ " + name + " lỗi", error);
                return ToolOutcome.Fail("Lỗi: " + error.Message);
            }
        }

        private static bool InputAllowed() { return Settings.Current.AllowInput; }

        private static ToolOutcome Off()
        {
            return ToolOutcome.Fail("Điều khiển chuột và bàn phím đang tắt trong Cài đặt.");
        }

        private static bool OnScreen(int x, int y)
        {
            System.Drawing.Rectangle b = System.Windows.Forms.SystemInformation.VirtualScreen;
            return x >= b.Left && x < b.Right && y >= b.Top && y < b.Bottom;
        }

        private static ToolOutcome OpenWebsite(string target)
        {
            target = (target ?? "").Trim();
            if (target.Length == 0) return ToolOutcome.Fail("Chưa có địa chỉ website.");
            string url;
            if (Regex.IsMatch(target, @"^[a-zA-Z][a-zA-Z0-9+.\-]*://")) url = target;
            else if (Regex.IsMatch(target, @"^(www\.)?[a-zA-Z0-9\-]+(\.[a-zA-Z0-9\-]+)+(/\S*)?$")) url = "https://" + target;
            else url = "https://www.google.com/search?q=" + Uri.EscapeDataString(target);
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return ToolOutcome.Fail("Chỉ mở được địa chỉ http hoặc https.");
            ProcessStartInfo info = new ProcessStartInfo(uri.AbsoluteUri);
            info.UseShellExecute = true;
            Process.Start(info);
            return ToolOutcome.Success("Đã mở " + uri.Host + ".");
        }
    }
}
