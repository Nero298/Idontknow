using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CapriHost.Core;

namespace CapriHost.Actions
{
    // Mở và đóng ứng dụng. Chỉ dùng Process.Start / CloseMainWindow, hoạt động trên mọi bản Windows.
    internal static class AppLauncher
    {
        private static readonly string[] Critical = { "explorer", "csrss", "winlogon", "wininit", "services", "lsass", "svchost", "system", "dwm", "smss", "taskhost", "taskhostw", "capri", "caprihost" };

        // Tên gọi khác -> tên chuẩn.
        private static string Canonical(string raw)
        {
            string n = (raw ?? "").Trim().ToLowerInvariant();
            switch (n)
            {
                case "google chrome": case "chrome browser": return "chrome";
                case "mozilla firefox": return "firefox";
                case "microsoft edge": case "msedge": return "edge";
                case "file explorer": case "windows explorer": case "this pc": case "my computer": case "files": return "explorer";
                case "calc": case "calculator": return "calculator";
                case "mspaint": case "ms paint": return "paint";
                case "task manager": case "taskmgr": return "taskmgr";
                case "visual studio code": case "vs code": case "code": return "vscode";
                case "ms word": case "microsoft word": return "word";
                case "ms excel": case "microsoft excel": return "excel";
                case "powerpnt": case "ms powerpoint": case "microsoft powerpoint": return "powerpoint";
                case "control": case "control panel": return "control";
            }
            return n;
        }

        // Danh sách ứng viên {đích, tham số}. Thử lần lượt cho đến khi mở được.
        private static List<string[]> Builtin(string name)
        {
            List<string[]> c = new List<string[]>();
            switch (name)
            {
                case "chrome":
                    c.Add(new[] { "%ProgramFiles%\\Google\\Chrome\\Application\\chrome.exe", "" });
                    c.Add(new[] { "%ProgramFiles(x86)%\\Google\\Chrome\\Application\\chrome.exe", "" });
                    c.Add(new[] { "%LOCALAPPDATA%\\Google\\Chrome\\Application\\chrome.exe", "" });
                    c.Add(new[] { "chrome", "" });
                    break;
                case "firefox":
                    c.Add(new[] { "%ProgramFiles%\\Mozilla Firefox\\firefox.exe", "" });
                    c.Add(new[] { "%ProgramFiles(x86)%\\Mozilla Firefox\\firefox.exe", "" });
                    c.Add(new[] { "firefox", "" });
                    break;
                case "edge":
                    c.Add(new[] { "msedge", "" });
                    c.Add(new[] { "microsoft-edge:", "" });
                    break;
                case "discord":
                    c.Add(new[] { "%LOCALAPPDATA%\\Discord\\Update.exe", "--processStart Discord.exe" });
                    c.Add(new[] { "discord://", "" });
                    break;
                case "spotify":
                    c.Add(new[] { "%APPDATA%\\Spotify\\Spotify.exe", "" });
                    c.Add(new[] { "spotify:", "" });
                    break;
                case "notepad": c.Add(new[] { "notepad.exe", "" }); break;
                case "calculator": c.Add(new[] { "calc.exe", "" }); break;
                case "explorer": c.Add(new[] { "explorer.exe", "" }); break;
                case "paint": c.Add(new[] { "mspaint.exe", "" }); break;
                case "taskmgr": c.Add(new[] { "taskmgr.exe", "" }); break;
                case "control": c.Add(new[] { "control.exe", "" }); break;
                case "word": c.Add(new[] { "winword", "" }); break;
                case "excel": c.Add(new[] { "excel", "" }); break;
                case "powerpoint": c.Add(new[] { "powerpnt", "" }); break;
                case "vscode":
                    c.Add(new[] { "%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe", "" });
                    c.Add(new[] { "code", "" });
                    break;
                case "steam": c.Add(new[] { "steam://open/main", "" }); break;
                case "zalo": c.Add(new[] { "%LOCALAPPDATA%\\Programs\\Zalo\\Zalo.exe", "" }); break;
            }
            return c;
        }

        private static string[] ProcessNames(string name)
        {
            switch (name)
            {
                case "chrome": return new[] { "chrome" };
                case "firefox": return new[] { "firefox" };
                case "edge": return new[] { "msedge" };
                case "discord": return new[] { "Discord" };
                case "spotify": return new[] { "Spotify" };
                case "notepad": return new[] { "notepad" };
                case "calculator": return new[] { "calc", "CalculatorApp", "Calculator" };
                case "paint": return new[] { "mspaint" };
                case "word": return new[] { "WINWORD" };
                case "excel": return new[] { "EXCEL" };
                case "powerpoint": return new[] { "POWERPNT" };
                case "vscode": return new[] { "Code" };
                case "steam": return new[] { "steam" };
                case "zalo": return new[] { "Zalo" };
                case "taskmgr": return new[] { "Taskmgr" };
            }
            return null;
        }

        // ---------------------------------------------------------------- mở

        internal static ToolOutcome Launch(string rawName, string extra)
        {
            string name = Canonical(rawName);
            if (name.Length == 0) return ToolOutcome.Fail("Chưa nói rõ mở ứng dụng nào.");

            // 1) Ứng dụng tuỳ chỉnh do người dùng khai báo (ưu tiên cao nhất).
            foreach (string line in (Settings.Current.CustomApps ?? "").Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (Canonical(line.Substring(0, eq)) != name) continue;
                if (TryStart(line.Substring(eq + 1).Trim(), extra)) return ToolOutcome.Success("Đã mở " + rawName + ".");
            }

            // 2a) Trình duyệt: dùng BrowserLauncher (tìm qua App Paths, không bao giờ rơi âm thầm về Internet Explorer).
            if (name == "chrome" || name == "firefox" || name == "edge")
            {
                ToolOutcome browser = BrowserLauncher.LaunchApp(name, extra);
                if (browser.Ok) return ToolOutcome.Success("Đã mở " + rawName + ".");
                return browser;
            }

            // 2) Ứng dụng có sẵn trong bảng.
            foreach (string[] candidate in Builtin(name))
            {
                string args = candidate[1].Length > 0 ? candidate[1] : extra;
                if (TryStart(candidate[0], args)) return ToolOutcome.Success("Đã mở " + rawName + ".");
            }

            // 3) Thử nhờ Windows tự tìm (App Paths / PATH).
            if (TryStart(rawName.Trim(), extra)) return ToolOutcome.Success("Đã mở " + rawName + ".");

            return ToolOutcome.Fail("Không tìm thấy ứng dụng '" + rawName + "' trên máy. Có thể thêm đường dẫn trong Cài đặt > Ứng dụng tuỳ chỉnh.");
        }

        private static bool IsUri(string target)
        {
            int colon = target.IndexOf(':');
            if (colon < 2) return false;                 // "C:\..." không phải URI
            for (int i = 0; i < colon; i++)
            {
                char c = target[i];
                if (!(char.IsLetterOrDigit(c) || c == '+' || c == '-' || c == '.')) return false;
            }
            return true;
        }

        private static bool TryStart(string target, string args)
        {
            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(target);
                if (expanded.Length == 0) return false;
                bool uri = IsUri(expanded);
                if (!uri && Path.IsPathRooted(expanded) && !File.Exists(expanded) && !Directory.Exists(expanded)) return false;
                ProcessStartInfo info = new ProcessStartInfo(expanded);
                info.UseShellExecute = true;
                if (!string.IsNullOrEmpty(args)) info.Arguments = Environment.ExpandEnvironmentVariables(args);
                Process.Start(info);
                return true;
            }
            catch (Win32Exception) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (FileNotFoundException) { return false; }
        }

        // ---------------------------------------------------------------- đóng

        internal static ToolOutcome Close(string rawName, bool force, ToolContext ctx)
        {
            string name = Canonical(rawName);
            if (name.Length == 0) return ToolOutcome.Fail("Chưa nói rõ đóng ứng dụng nào.");
            if (name == "explorer") return ToolOutcome.Fail("Không đóng File Explorer theo cách này vì sẽ làm mất thanh tác vụ.");

            List<Process> targets = new List<Process>();
            string[] known = ProcessNames(name);
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    string pn = p.ProcessName;
                    if (IsCritical(pn)) continue;
                    bool match = false;
                    if (known != null)
                    {
                        foreach (string k in known) if (string.Equals(pn, k, StringComparison.OrdinalIgnoreCase)) match = true;
                    }
                    else
                    {
                        if (pn.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) match = true;
                        else if (p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.IndexOf(rawName.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) match = true;
                    }
                    if (match) targets.Add(p); else p.Dispose();
                }
                catch (InvalidOperationException) { /* tiến trình vừa thoát: bỏ qua */ }
                catch (Win32Exception) { /* không đọc được tiến trình hệ thống: bỏ qua */ }
            }
            if (targets.Count == 0) return ToolOutcome.Fail("Không thấy '" + rawName + "' đang chạy.");

            if (force && !Tools.Allowed(ctx, "Buộc tắt '" + rawName + "'? Dữ liệu chưa lưu sẽ mất.")) return ToolOutcome.Fail("Người dùng không cho phép buộc tắt.");

            int closed = 0, stuck = 0;
            foreach (Process p in targets)
            {
                try
                {
                    if (force) { p.Kill(); p.WaitForExit(1500); closed++; }
                    else if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        p.CloseMainWindow();
                        if (p.WaitForExit(1500)) closed++; else stuck++;
                    }
                }
                catch (InvalidOperationException) { closed++; }
                catch (Win32Exception) { stuck++; }
                finally { p.Dispose(); }
            }
            if (stuck > 0 && closed == 0) return ToolOutcome.Fail("Đã gửi lệnh đóng '" + rawName + "' nhưng nó chưa tắt (có thể đang hỏi lưu file).");
            return ToolOutcome.Success("Đã đóng '" + rawName + "'" + (stuck > 0 ? " (một số cửa sổ chưa tắt)." : "."));
        }

        private static bool IsCritical(string processName)
        {
            foreach (string c in Critical) if (string.Equals(processName, c, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ---------------------------------------------------------------- liệt kê cửa sổ

        internal static string ListWindows()
        {
            List<string> lines = new List<string>();
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0 && !IsCritical(p.ProcessName))
                        lines.Add(p.MainWindowTitle + " (" + p.ProcessName + ")");
                }
                catch (InvalidOperationException) { /* tiến trình vừa thoát */ }
                catch (Win32Exception) { /* không đọc được: bỏ qua */ }
                finally { p.Dispose(); }
            }
            if (lines.Count == 0) return "Không có cửa sổ nào đang mở.";
            if (lines.Count > 25) lines.RemoveRange(25, lines.Count - 25);
            return string.Join("\n", lines.ToArray());
        }

        internal static string ActiveWindow()
        {
            IntPtr hwnd = Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "Không có cửa sổ nào đang được chọn.";
            System.Text.StringBuilder title = new System.Text.StringBuilder(256);
            Native.GetWindowText(hwnd, title, 256);
            string proc = "?";
            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                using (Process p = Process.GetProcessById((int)pid)) proc = p.ProcessName;
            }
            catch (ArgumentException) { /* tiến trình đã thoát: giữ "?" */ }
            catch (InvalidOperationException) { /* như trên */ }
            return title + " (" + proc + ")";
        }
    }
}
