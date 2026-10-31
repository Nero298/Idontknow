using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using CapriHost.Core;

namespace CapriHost.Actions
{
    // Phóng to / thu nhỏ / khôi phục / toàn màn hình (F11) cho cửa sổ đang ở trước hoặc cửa sổ tìm theo tên.
    // Chỉ dùng user32 (EnumWindows, ShowWindow, SetForegroundWindow) - có từ Windows 2000, không UIA, không API Win8+.
    internal static class WindowControl
    {
        private sealed class Found
        {
            internal IntPtr Handle;
            internal string Title = "";
            internal string Process = "";
        }

        internal static ToolOutcome Run(string action, string target)
        {
            action = (action ?? "").Trim().ToLowerInvariant();
            if (action == "max") action = "maximize";
            if (action == "min") action = "minimize";
            if (action != "maximize" && action != "minimize" && action != "restore" && action != "fullscreen")
                return ToolOutcome.Fail("action phải là maximize, minimize, restore hoặc fullscreen.");

            Found window = Pick((target ?? "").Trim());
            if (window == null)
                return ToolOutcome.Fail(string.IsNullOrEmpty(target)
                    ? "Không có cửa sổ nào đang ở phía trước để điều khiển."
                    : "Không tìm thấy cửa sổ '" + target + "'. Thử gọi list_windows để xem tên đúng.");

            string label = "'" + Short(window.Title) + "' (" + window.Process + ")";
            switch (action)
            {
                case "maximize":
                    Native.ShowWindow(window.Handle, Native.SW_MAXIMIZE);
                    return Verify(window.Handle, label, "phóng to", Native.IsZoomed(window.Handle));
                case "minimize":
                    Native.ShowWindow(window.Handle, Native.SW_MINIMIZE);
                    return Verify(window.Handle, label, "thu nhỏ", Native.IsIconic(window.Handle));
                case "restore":
                    Native.ShowWindow(window.Handle, Native.SW_RESTORE);
                    return Verify(window.Handle, label, "khôi phục", !Native.IsIconic(window.Handle) && !Native.IsZoomed(window.Handle));
            }

            // fullscreen = F11 trên cửa sổ đó: phải đưa nó lên trước rồi mới gửi phím.
            if (Native.IsIconic(window.Handle)) Native.ShowWindow(window.Handle, Native.SW_RESTORE);
            if (!Foreground(window.Handle))
                return ToolOutcome.Fail("Windows không cho đưa " + label + " lên trước (đang có cửa sổ khác giữ quyền). Bấm vào cửa sổ đó một lần rồi thử lại.");
            Thread.Sleep(120);
            InputControl.Tap(0x7A);       // F11
            return ToolOutcome.Success("Đã bấm F11 (bật/tắt toàn màn hình) cho " + label + ".");
        }

        private static ToolOutcome Verify(IntPtr handle, string label, string what, bool ok)
        {
            // Cho cửa sổ một nhịp để đổi trạng thái rồi đọc lại - không báo "xong" nếu chưa thật sự đổi.
            Thread.Sleep(150);
            return ok ? ToolOutcome.Success("Đã " + what + " " + label + ".")
                      : ToolOutcome.Fail("Đã gửi lệnh " + what + " tới " + label + " nhưng cửa sổ không đổi trạng thái (có thể ứng dụng không cho phép).");
        }

        private static string Short(string title)
        {
            return title.Length <= 50 ? title : title.Substring(0, 49) + "…";
        }

        // Đưa cửa sổ lên trước, vượt cơ chế chặn của Windows bằng AttachThreadInput (có từ Win2000).
        private static bool Foreground(IntPtr target)
        {
            IntPtr front = Native.GetForegroundWindow();
            if (front == target) return true;
            uint pid;
            uint frontThread = front == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(front, out pid);
            uint myThread = Native.GetCurrentThreadId();
            bool attached = false;
            try
            {
                if (frontThread != 0 && frontThread != myThread) attached = Native.AttachThreadInput(myThread, frontThread, true);
                Native.BringWindowToTop(target);
                Native.SetForegroundWindow(target);
            }
            finally
            {
                if (attached) Native.AttachThreadInput(myThread, frontThread, false);
            }
            for (int i = 0; i < 6; i++)
            {
                if (Native.GetForegroundWindow() == target) return true;
                Thread.Sleep(50);
            }
            return false;
        }

        private static Found Pick(string target)
        {
            if (target.Length == 0)
            {
                IntPtr front = Native.GetForegroundWindow();
                if (front == IntPtr.Zero) return null;
                Found f = Describe(front);
                // Nếu cửa sổ trước là Capri (vừa bấm trong chat trên PC) thì lấy cửa sổ kế tiếp trong thứ tự Z.
                if (f != null && f.Process.Equals(Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase)) return NextAfterSelf();
                return f;
            }

            string lower = target.ToLowerInvariant();
            Found best = null;
            foreach (Found w in All())
            {
                if (w.Process.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                    w.Process.ToLowerInvariant().IndexOf(lower, StringComparison.Ordinal) >= 0 ||
                    w.Title.ToLowerInvariant().IndexOf(lower, StringComparison.Ordinal) >= 0)
                {
                    best = w;        // EnumWindows đi từ trên xuống theo thứ tự Z: lấy cái đầu tiên
                    break;
                }
            }
            return best;
        }

        private static Found NextAfterSelf()
        {
            foreach (Found w in All()) return w;
            return null;
        }

        private static Found Describe(IntPtr handle)
        {
            uint pid;
            Native.GetWindowThreadProcessId(handle, out pid);
            Found found = new Found();
            found.Handle = handle;
            int length = Native.GetWindowTextLength(handle);
            if (length > 0)
            {
                StringBuilder text = new StringBuilder(length + 2);
                Native.GetWindowText(handle, text, text.Capacity);
                found.Title = text.ToString();
            }
            try
            {
                using (Process p = Process.GetProcessById((int)pid)) found.Process = p.ProcessName;
            }
            catch (ArgumentException) { found.Process = "?"; }                 // tiến trình đã thoát
            catch (InvalidOperationException) { found.Process = "?"; }
            return found;
        }

        // Mọi cửa sổ cấp cao đang hiện, có tiêu đề, không phải của Capri và không phải thanh tác vụ/desktop.
        private static List<Found> All()
        {
            List<Found> list = new List<Found>();
            uint self = (uint)Process.GetCurrentProcess().Id;
            Native.EnumWindows(delegate(IntPtr handle, IntPtr param)
            {
                if (!Native.IsWindowVisible(handle)) return true;
                if (Native.GetWindowTextLength(handle) == 0) return true;
                uint pid;
                Native.GetWindowThreadProcessId(handle, out pid);
                if (pid == self) return true;
                StringBuilder cls = new StringBuilder(64);
                Native.GetClassName(handle, cls, 64);
                string c = cls.ToString();
                if (c == "Shell_TrayWnd" || c == "Progman" || c == "WorkerW") return true;
                Found f = Describe(handle);
                if (f != null) list.Add(f);
                return true;
            }, IntPtr.Zero);
            return list;
        }
    }
}
