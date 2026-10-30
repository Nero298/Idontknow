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
        [STAThread]
        private static void Main()
        {
            bool first;
            using (Mutex single = new Mutex(true, "Local\\CapriHost.SingleInstance", out first))
            {
                if (!first)
                {
                    MessageBox.Show("Capri Host đang chạy rồi (xem biểu tượng ở khay hệ thống).", "Capri Host", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Error("Lỗi giao diện", e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log.Error("Lỗi không bắt được", e.ExceptionObject as Exception);
                };
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Settings.Current.Load();
                Http.Init();                 // bật TLS 1.2 cho Windows 7
                Log.Info("Capri Host " + Settings.Version + " khởi động trên " + Environment.OSVersion.VersionString);

                Application.Run(new TrayApp());
                GC.KeepAlive(single);
            }
        }
    }
}
