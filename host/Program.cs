using System;
using System.Threading;
using System.Windows.Forms;
using CapriHost.Ai;
using CapriHost.Core;
using CapriHost.Ui;

namespace CapriHost
{
    internal static class Program
    {
        // Tên các đối tượng dùng chung giữa các lần mở: lần mở thứ hai chỉ "gõ cửa" bản đang chạy rồi thoát.
        private const string MutexName = "Local\\CapriHost.SingleInstance";
        private const string ShowEventName = "Local\\CapriHost.Show";

        [STAThread]
        private static void Main(string[] args)
        {
            bool autostart = Array.IndexOf(args, "--autostart") >= 0;
            bool first;
            using (Mutex single = new Mutex(true, MutexName, out first))
            using (EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
            {
                if (!first)
                {
                    // Đã có Capri đang chạy: đưa cửa sổ của nó lên (trừ khi đây là lần tự khởi động trùng).
                    if (!autostart) show.Set();
                    return;
                }

                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Error("Giao diện", "Lỗi giao diện", e.Exception, null); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log.Error("Host", "Lỗi không bắt được" + (e.IsTerminating ? " (chương trình sẽ đóng)" : ""), e.ExceptionObject as Exception, null);
                };
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

                // Mỗi lệnh từ điện thoại chiếm một luồng trong lúc chờ AI/mạng: tăng số luồng tối thiểu để lệnh mới không phải xếp hàng.
                ThreadPool.SetMinThreads(32, 32);

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Settings.Current.Load();
                Http.Init();                 // bật TLS 1.2 cho Windows 7
                Log.Info("Khởi động", "Capri Host " + Settings.Version + " (giao thức v" + Settings.ProtocolVersion + ") trên " +
                    Environment.OSVersion.VersionString + (autostart ? " - tự khởi động cùng Windows" : ""), null);

                // Lần đầu chạy: bật sẵn "Khởi động cùng Windows" (tắt được trong cửa sổ chính).
                bool firstRun = !Settings.Current.FirstRunDone;
                if (firstRun)
                {
                    bool ok = Autostart.Set(true);
                    Log.Info("Khởi động", ok ? "Đã bật tự khởi động cùng Windows" : "Chưa bật được tự khởi động cùng Windows", null);
                    Settings.Current.FirstRunDone = true;
                    Settings.Current.Save();
                }
                else Autostart.Heal();

                TrayApp app = new TrayApp(autostart && !firstRun, firstRun, show);
                Application.Run(app);
                GC.KeepAlive(single);
            }
        }
    }
}
