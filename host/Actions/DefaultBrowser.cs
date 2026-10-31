using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CapriHost.Core;
using Microsoft.Win32;

namespace CapriHost.Actions
{
    // "Đặt Chrome làm trình duyệt mặc định" - TRUNG THỰC: chỉ báo thành công khi đọc lại thấy đã đổi.
    // Windows 7: dùng giao diện COM IApplicationAssociationRegistration (đường chính thức), nếu chưa đổi được thì ghi UserChoice trong
    // HKCU và đọc lại. Từ Windows 8 trở đi Microsoft chặn việc này (hash UserChoice) - khi đó mở trang cài đặt để người dùng tự bấm.
    internal static class DefaultBrowser
    {
        [ComImport, Guid("4E530B0A-E611-4C77-A3AC-9031D022281B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationAssociationRegistration
        {
            [PreserveSig] int QueryCurrentDefault([MarshalAs(UnmanagedType.LPWStr)] string query, int type, int level,
                [MarshalAs(UnmanagedType.LPWStr)] out string association);
            [PreserveSig] int QueryAppIsDefault([MarshalAs(UnmanagedType.LPWStr)] string query, int type, int level,
                [MarshalAs(UnmanagedType.LPWStr)] string appName, [MarshalAs(UnmanagedType.Bool)] out bool isDefault);
            [PreserveSig] int QueryAppIsDefaultAll(int level, [MarshalAs(UnmanagedType.LPWStr)] string appName,
                [MarshalAs(UnmanagedType.Bool)] out bool isDefault);
            [PreserveSig] int SetAppAsDefault([MarshalAs(UnmanagedType.LPWStr)] string appName, [MarshalAs(UnmanagedType.LPWStr)] string set, int type);
            [PreserveSig] int SetAppAsDefaultAll([MarshalAs(UnmanagedType.LPWStr)] string appName);
            [PreserveSig] int ClearUserAssociations();
        }

        [ComImport, Guid("591209C7-767B-42B2-9FBA-44EE4615F2C7")]
        private class ApplicationAssociationRegistration { }

        private const int AT_URLPROTOCOL = 1;
        private const int AL_EFFECTIVE = 1;

        internal static ToolOutcome SetChrome(ToolContext ctx)
        {
            string chrome = BrowserLauncher.FindChrome();
            if (chrome == null) return ToolOutcome.Fail("Không tìm thấy Chrome trên máy nên không đặt làm mặc định được. Hãy cài Chrome trước.");
            if (!Tools.Allowed(ctx, "Đặt Google Chrome làm trình duyệt mặc định của Windows?"))
                return ToolOutcome.Fail("Người dùng không cho phép đổi trình duyệt mặc định.");

            string before = BrowserLauncher.DefaultProgId();
            if (IsChromeProgId(before)) return ToolOutcome.Success("Chrome đã là trình duyệt mặc định rồi (" + before + ").");

            string attempts = "";
            // 1) COM chính thức (Win7 cho phép, Win8+ trả thành công giả -> đọc lại sẽ lộ ra).
            string comError = TryCom();
            if (comError != null) attempts += "COM: " + comError + ". ";
            if (IsChromeProgId(BrowserLauncher.DefaultProgId()))
                return ToolOutcome.Success("Đã đặt Chrome làm trình duyệt mặc định (đã đọc lại để kiểm tra).");

            // 2) Ghi UserChoice trong HKCU (Win7 không kiểm hash).
            string regError = TryRegistry();
            if (regError != null) attempts += "Registry: " + regError + ". ";
            if (IsChromeProgId(BrowserLauncher.DefaultProgId()))
                return ToolOutcome.Success("Đã đặt Chrome làm trình duyệt mặc định (đã đọc lại để kiểm tra).");

            // 3) Không đổi được: nói thật và mở đúng trang cài đặt cho người dùng tự bấm.
            string guided = OpenSettingsPage(chrome);
            Log.Warn("Trình duyệt", "Không tự đặt được Chrome làm mặc định. " + attempts, null);
            return ToolOutcome.Fail("Windows không cho Capri tự đổi (" + attempts.Trim() + "). " + guided);
        }

        private static bool IsChromeProgId(string progId)
        {
            return progId != null && progId.StartsWith("ChromeHTML", StringComparison.OrdinalIgnoreCase);
        }

        // Tên đăng ký của Chrome trong StartMenuInternet (thường là "Google Chrome").
        private static string RegisteredName()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet"))
                {
                    if (key != null)
                        foreach (string sub in key.GetSubKeyNames())
                            if (sub.IndexOf("chrome", StringComparison.OrdinalIgnoreCase) >= 0) return sub;
                }
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet"))
                {
                    if (key != null)
                        foreach (string sub in key.GetSubKeyNames())
                            if (sub.IndexOf("chrome", StringComparison.OrdinalIgnoreCase) >= 0) return sub;
                }
            }
            catch (System.Security.SecurityException) { /* dùng tên mặc định bên dưới */ }
            catch (UnauthorizedAccessException) { /* như trên */ }
            return "Google Chrome";
        }

        private static string TryCom()
        {
            try
            {
                IApplicationAssociationRegistration reg = (IApplicationAssociationRegistration)new ApplicationAssociationRegistration();
                int hr = reg.SetAppAsDefaultAll(RegisteredName());
                return hr == 0 ? null : "mã 0x" + hr.ToString("X");
            }
            catch (COMException error) { return "COM lỗi 0x" + error.ErrorCode.ToString("X"); }
            catch (InvalidCastException error) { return error.Message; }
            catch (UnauthorizedAccessException error) { return error.Message; }
        }

        private static string TryRegistry()
        {
            try
            {
                foreach (string scheme in new string[] { "http", "https" })
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\" + scheme + @"\UserChoice"))
                    {
                        if (key == null) return "không mở được khoá " + scheme;
                        key.SetValue("ProgId", "ChromeHTML");
                    }
                }
                foreach (string ext in new string[] { ".htm", ".html" })
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + ext + @"\UserChoice"))
                    {
                        if (key == null) return "không mở được khoá " + ext;
                        key.SetValue("ProgId", "ChromeHTML");
                    }
                }
                return null;
            }
            catch (UnauthorizedAccessException error) { return "bị từ chối quyền (" + error.Message + ")"; }
            catch (System.Security.SecurityException error) { return "bị từ chối quyền (" + error.Message + ")"; }
            catch (System.IO.IOException error) { return error.Message; }
        }

        private static string OpenSettingsPage(string chromeExe)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(chromeExe, "chrome://settings/defaultBrowser");
                info.UseShellExecute = false;
                using (Process.Start(info)) { }
                return "Mình đã mở trang cài đặt của Chrome: hãy bấm 'Đặt làm mặc định' ở đó.";
            }
            catch (Win32Exception) { /* thử bảng điều khiển bên dưới */ }
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("control.exe", "/name Microsoft.DefaultPrograms");
                info.UseShellExecute = true;
                using (Process.Start(info)) { }
                return "Mình đã mở 'Default Programs' của Windows: chọn Google Chrome rồi 'Set this program as default'.";
            }
            catch (Win32Exception error)
            {
                return "Cũng không mở được trang cài đặt (" + error.Message + "). Hãy vào Control Panel > Default Programs.";
            }
        }
    }
}
