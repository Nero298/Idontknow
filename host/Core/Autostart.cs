using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CapriHost.Core
{
    // Tự khởi động cùng Windows: ghi vào HKCU\...\Run (không cần quyền Admin, chạy được trên Windows 7).
    // Lệnh có "--autostart" để Capri biết là mở do khởi động máy => chạy êm ở khay hệ thống, không bật cửa sổ chính.
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "CapriHost";

        internal static string Command
        {
            get { return "\"" + Application.ExecutablePath + "\" --autostart"; }
        }

        private static string Current()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key == null ? null : key.GetValue(ValueName) as string;
            }
            catch (System.Security.SecurityException error) { Log.Warn("Khởi động", "Không đọc được mục tự khởi động: " + error.Message, null); }
            catch (UnauthorizedAccessException error) { Log.Warn("Khởi động", "Không đọc được mục tự khởi động: " + error.Message, null); }
            return null;
        }

        internal static bool IsOn()
        {
            return Current() != null;
        }

        // Bật/tắt. Trả về true nếu đọc lại thấy đúng trạng thái mong muốn.
        internal static bool Set(bool on)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) { Log.Error("Khởi động", "Không mở được khoá Run của Windows", null, null); return false; }
                    if (on) key.SetValue(ValueName, Command);
                    else key.DeleteValue(ValueName, false);
                }
            }
            catch (UnauthorizedAccessException error) { Log.Error("Khởi động", "Không đổi được 'Khởi động cùng Windows'", error, null); return false; }
            catch (System.Security.SecurityException error) { Log.Error("Khởi động", "Không đổi được 'Khởi động cùng Windows'", error, null); return false; }
            bool now = IsOn();
            if (now != on) Log.Warn("Khởi động", "Đã ghi mục tự khởi động nhưng đọc lại không khớp (có thể phần mềm diệt virus chặn)", null);
            return now == on;
        }

        // Nếu người dùng đã chuyển/đổi tên thư mục chứa CapriHost.exe thì sửa lại đường dẫn cho khỏi hỏng.
        internal static void Heal()
        {
            string value = Current();
            if (value == null) return;
            if (!string.Equals(value, Command, StringComparison.OrdinalIgnoreCase))
            {
                Log.Info("Khởi động", "Cập nhật đường dẫn tự khởi động: " + Command, null);
                Set(true);
            }
        }
    }
}
