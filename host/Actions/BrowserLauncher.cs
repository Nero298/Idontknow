using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CapriHost.Core;
using Microsoft.Win32;

namespace CapriHost.Actions
{
    // MỘT chỗ duy nhất mở địa chỉ web. Không bao giờ âm thầm rơi về Internet Explorer.
    // Thứ tự: cài đặt "Trình duyệt" (chrome | firefox | default | đường dẫn riêng) -> nếu không thấy thì cảnh báo (log + câu trả lời)
    // rồi dùng trình duyệt mặc định của Windows, TRỪ khi nó là Internet Explorer (khi đó từ chối thay vì mở IE).
    internal static class BrowserLauncher
    {
        // Chrome 109 là bản cuối chạy trên Windows 7: dùng làm User-Agent khi tải trang YouTube.
        internal const string ChromeUserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36";

        // ---------------------------------------------------------------- tìm trình duyệt

        private static string AppPath(string exe)
        {
            foreach (RegistryView view in new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (RegistryHive hive in new RegistryHive[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                {
                    try
                    {
                        using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                        {
                            if (key == null) continue;
                            string path = key.GetValue("") as string;
                            if (!string.IsNullOrEmpty(path))
                            {
                                path = path.Trim('"');
                                if (File.Exists(path)) return path;
                            }
                        }
                    }
                    catch (System.Security.SecurityException) { /* không có quyền đọc nhánh này: thử nhánh khác */ }
                    catch (UnauthorizedAccessException) { /* như trên */ }
                    catch (IOException) { /* như trên */ }
                }
            }
            return null;
        }

        private static string FirstExisting(params string[] candidates)
        {
            foreach (string c in candidates)
            {
                string path = Environment.ExpandEnvironmentVariables(c);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        internal static string FindChrome()
        {
            return AppPath("chrome.exe") ?? FirstExisting(
                @"%ProgramFiles%\Google\Chrome\Application\chrome.exe",
                @"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe",
                @"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe");
        }

        internal static string FindFirefox()
        {
            return AppPath("firefox.exe") ?? FirstExisting(
                @"%ProgramFiles%\Mozilla Firefox\firefox.exe",
                @"%ProgramFiles(x86)%\Mozilla Firefox\firefox.exe");
        }

        internal static string FindEdge()
        {
            return AppPath("msedge.exe") ?? FirstExisting(
                @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe",
                @"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe");
        }

        // Trình duyệt mặc định hiện tại của Windows (ProgId của http). Rỗng nếu không đọc được.
        internal static string DefaultProgId()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice"))
                {
                    if (key != null) return (key.GetValue("ProgId") as string) ?? "";
                }
            }
            catch (System.Security.SecurityException) { /* không đọc được: coi như không biết */ }
            catch (UnauthorizedAccessException) { /* như trên */ }
            return "";
        }

        internal static bool DefaultIsInternetExplorer()
        {
            string prog = DefaultProgId();
            return prog.StartsWith("IE.", StringComparison.OrdinalIgnoreCase) || prog.Length == 0 && IeIsLikelyDefault();
        }

        private static bool IeIsLikelyDefault()
        {
            // Máy Win7 chưa từng chọn trình duyệt: không có UserChoice -> mặc định là IE.
            return Environment.OSVersion.Version.Major < 10;
        }

        // ---------------------------------------------------------------- mở

        // preference: "" = theo cài đặt; "chrome" | "firefox" | "edge" | "default" | đường dẫn .exe.
        internal static ToolOutcome Open(string url, string preference)
        {
            string choice = (preference ?? "").Trim();
            if (choice.Length == 0) choice = (Settings.Current.Browser ?? "chrome").Trim();
            if (choice.Length == 0) choice = "chrome";
            string lower = choice.ToLowerInvariant();

            string exe = null;
            string name = choice;
            if (lower == "chrome" || lower == "google chrome") { name = "Chrome"; exe = FindChrome(); }
            else if (lower == "firefox") { name = "Firefox"; exe = FindFirefox(); }
            else if (lower == "edge" || lower == "msedge") { name = "Edge"; exe = FindEdge(); }
            else if (lower == "default") { return OpenDefault(url, ""); }
            else
            {
                string path = Environment.ExpandEnvironmentVariables(choice.Trim('"'));
                if (File.Exists(path)) { name = Path.GetFileNameWithoutExtension(path); exe = path; }
                else name = choice;
            }

            if (exe != null)
            {
                ToolOutcome started = Start(exe, url, name);
                if (started.Ok) return started;
                Log.Warn("Trình duyệt", "Không chạy được " + name + " (" + started.Text + ")", null);
            }
            else
            {
                Log.Warn("Trình duyệt", "Không tìm thấy " + name + " trên máy này", null);
            }
            string notice = "Không tìm thấy " + name + " (hoặc không chạy được).";
            return OpenDefault(url, notice);
        }

        private static ToolOutcome Start(string exe, string url, string name)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(exe);
                info.Arguments = string.IsNullOrEmpty(url) ? "" : "\"" + url + "\"";
                info.UseShellExecute = false;
                using (Process.Start(info)) { }
                return ToolOutcome.Success("Đã mở bằng " + name + ".");
            }
            catch (Win32Exception error) { return ToolOutcome.Fail(error.Message); }
            catch (InvalidOperationException error) { return ToolOutcome.Fail(error.Message); }
        }

        // Trình duyệt mặc định của Windows. Nếu nó là IE thì KHÔNG mở (người dùng không muốn IE).
        private static ToolOutcome OpenDefault(string url, string notice)
        {
            string prefix = notice.Length > 0 ? notice + " " : "";
            if (DefaultIsInternetExplorer())
            {
                string text = prefix + "Trình duyệt mặc định của Windows đang là Internet Explorer nên Capri không mở. " +
                              "Cài Chrome, hoặc bấm 'Đặt Chrome làm trình duyệt mặc định' trong Cài đặt, hoặc chọn trình duyệt khác ở mục Trình duyệt.";
                Log.Warn("Trình duyệt", text, null);
                return ToolOutcome.Fail(text);
            }
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(url);
                info.UseShellExecute = true;
                using (Process.Start(info)) { }
            }
            catch (Win32Exception error)
            {
                Log.Warn("Trình duyệt", "Không mở được trình duyệt mặc định: " + error.Message, null);
                return ToolOutcome.Fail(prefix + "Không mở được trình duyệt mặc định: " + error.Message);
            }
            return ToolOutcome.Success(prefix + "Đã mở bằng trình duyệt mặc định của Windows.");
        }

        // Mở ứng dụng trình duyệt theo tên (open_app chrome/firefox/edge), kèm địa chỉ nếu có.
        internal static ToolOutcome LaunchApp(string name, string urlOrEmpty)
        {
            string exe = name == "chrome" ? FindChrome() : name == "firefox" ? FindFirefox() : FindEdge();
            string label = name == "chrome" ? "Chrome" : name == "firefox" ? "Firefox" : "Edge";
            if (exe == null)
            {
                Log.Warn("Trình duyệt", "Không tìm thấy " + label, null);
                return ToolOutcome.Fail("Không tìm thấy " + label + " trên máy này.");
            }
            return Start(exe, urlOrEmpty, label);
        }
    }
}
