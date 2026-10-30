using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using CapriHost.Core;
using CapriHost.Network;

namespace CapriHost.Ui
{
    // Vỏ của ứng dụng: biểu tượng khay hệ thống, pet, máy chủ LAN. Không có cửa sổ chính, chạy nền.
    internal sealed class TrayApp : ApplicationContext
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly Hub hub = new Hub();
        private readonly CapriServer server;
        private readonly Discovery discovery;
        private readonly PetForm pet;
        private readonly ChatBox chat;
        private readonly Timer housekeeping = new Timer();
        private readonly ToolStripMenuItem addressItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem togglePet = new ToolStripMenuItem();
        private readonly ToolStripMenuItem autostart = new ToolStripMenuItem();
        private readonly Control sync = new Control();     // chỉ để chuyển việc về luồng giao diện
        private PairForm pairForm;

        internal TrayApp()
        {
            IntPtr forceHandle = sync.Handle;
            Settings settings = Settings.Current;
            server = new CapriServer(settings.Port);
            discovery = new Discovery(settings.Port);

            BuildMenu();
            tray.Icon = LoadIcon();
            tray.Text = "Capri Host";
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { chat.Open(); };
            tray.Visible = true;

            pet = new PetForm(menu);
            chat = new ChatBox(hub);
            pet.OpenChat += delegate { chat.Open(); };

            hub.Server = server;
            hub.Said += delegate(string text) { OnUi(delegate { if (Settings.Current.PetVisible) pet.Say(text); }); };
            hub.Activity += delegate(string text, int holdMs) { OnUi(delegate { ShowActivity(text, holdMs); }); };
            hub.LocalConfirm = ConfirmOnPc;
            hub.SettingsChanged += delegate { OnUi(ApplyPetVisibility); };

            server.Message += hub.Handle;
            server.Connected += delegate(ClientSession s)
            {
                hub.OnConnected(s);
                OnUi(delegate { UpdateStatus(); tray.ShowBalloonTip(2500, "Capri", s.DeviceName + " đã kết nối", ToolTipIcon.Info); });
            };
            server.Disconnected += delegate { OnUi(UpdateStatus); };
            Pairing.Changed += delegate { OnUi(UpdateStatus); };

            if (!server.Start())
            {
                MessageBox.Show(
                    "Cổng " + settings.Port + " đang bị chương trình khác dùng nên Capri không nhận được kết nối từ điện thoại.\nĐóng chương trình để khởi động lại và thử lại.",
                    "Capri Host",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            discovery.Start();

            // Trả RAM cho Windows mỗi 90 giây (máy yếu).
            housekeeping.Interval = 90000;
            housekeeping.Tick += delegate { Native.TrimMemory(); };
            housekeeping.Start();

            Application.Idle += FirstIdle;
        }

        // Chạy một lần khi vòng lặp thông điệp đã sẵn sàng.
        private void FirstIdle(object sender, EventArgs e)
        {
            Application.Idle -= FirstIdle;
            ApplyPetVisibility();
            UpdateStatus();
            if (Settings.Current.Devices.Count == 0) ShowPairing();
            Timer greet = new Timer();
            greet.Interval = 1200;
            greet.Tick += delegate { greet.Stop(); greet.Dispose(); hub.GreetLocal(); Native.TrimMemory(); };
            greet.Start();
        }

        // ---------------------------------------------------------------- menu

        private void BuildMenu()
        {
            ToolStripMenuItem talk = new ToolStripMenuItem("Nói chuyện với Capri...");
            talk.Click += delegate { chat.Open(); };

            ToolStripMenuItem pair = new ToolStripMenuItem("Ghép đôi điện thoại mới...");
            pair.Click += delegate { ShowPairing(); };

            addressItem.Enabled = false;

            togglePet.Click += delegate
            {
                Settings.Current.PetVisible = !Settings.Current.PetVisible;
                Settings.Current.Save();
                ApplyPetVisibility();
            };

            autostart.CheckOnClick = false;
            autostart.Text = "Khởi động cùng Windows";
            autostart.Click += delegate { SetAutostart(!IsAutostart()); };

            ToolStripMenuItem forget = new ToolStripMenuItem("Xoá tất cả điện thoại đã ghép");
            forget.Click += delegate
            {
                if (MessageBox.Show("Xoá mọi điện thoại đã ghép đôi? Bạn sẽ phải nhập mã lại.", "Capri Host", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    Settings.Current.RemoveAllDevices();
                    server.Stop();
                    server.Start();
                    UpdateStatus();
                }
            };

            ToolStripMenuItem folder = new ToolStripMenuItem("Mở thư mục dữ liệu / nhật ký");
            folder.Click += delegate { OpenFolder(); };

            ToolStripMenuItem quit = new ToolStripMenuItem("Thoát");
            quit.Click += delegate { Quit(); };

            menu.Items.Add(talk);
            menu.Items.Add(pair);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(addressItem);
            menu.Items.Add(togglePet);
            menu.Items.Add(autostart);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(forget);
            menu.Items.Add(folder);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(quit);
            menu.Opening += delegate { UpdateStatus(); };
        }

        private void UpdateStatus()
        {
            List<string> ips = Discovery.LocalAddresses();
            string first = ips.Count > 0 ? ips[0] : "?";
            addressItem.Text = "Địa chỉ: " + first + ":" + Settings.Current.Port + "  (" + server.ClientCount + " điện thoại)";
            togglePet.Text = Settings.Current.PetVisible ? "Ẩn Capri" : "Hiện Capri";
            autostart.Checked = IsAutostart();
            string tip = "Capri Host - " + server.ClientCount + " điện thoại";
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        // Bong bóng hoạt động cạnh sprite; nếu đang ẩn Capri thì hiện thông báo ở khay hệ thống.
        private void ShowActivity(string text, int holdMs)
        {
            if (Settings.Current.PetVisible) pet.Notice(text, holdMs);
            else tray.ShowBalloonTip(Math.Max(1500, holdMs), "Capri", text, ToolTipIcon.None);
        }

        private void ApplyPetVisibility()
        {
            if (Settings.Current.PetVisible) pet.ShowPet(); else pet.HidePet();
            UpdateStatus();
        }

        private void ShowPairing()
        {
            if (pairForm != null && !pairForm.IsDisposed) { pairForm.Activate(); return; }
            pairForm = new PairForm(Settings.Current.Port);
            pairForm.Show();
        }

        // ---------------------------------------------------------------- tiện ích

        // Chuyển việc sang luồng giao diện (an toàn khi gọi từ luồng mạng).
        private void OnUi(Action action)
        {
            try { sync.BeginInvoke(action); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        // Hỏi xác nhận bằng hộp thoại trên PC (gọi từ luồng nền nên phải Invoke).
        private bool ConfirmOnPc(string question)
        {
            bool answer = false;
            try
            {
                sync.Invoke(new Action(delegate
                {
                    answer = MessageBox.Show(question, "Capri hỏi", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                }));
            }
            catch (InvalidOperationException) { }
            return answer;
        }

        private static Icon LoadIcon()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.ico"))
                    if (stream != null) return new Icon(stream, 16, 16);
            }
            catch (Exception) { }
            return SystemIcons.Application;
        }

        private static string ExePath { get { return Application.ExecutablePath; } }

        private static bool IsAutostart()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && key.GetValue("CapriHost") != null;
            }
            catch (Exception) { return false; }
        }

        private static void SetAutostart(bool on)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) key.SetValue("CapriHost", "\"" + ExePath + "\"");
                    else key.DeleteValue("CapriHost", false);
                }
            }
            catch (Exception error) { Log.Error("Không đổi được khởi động cùng Windows", error); }
        }

        private static void OpenFolder()
        {
            try { Process.Start("explorer.exe", "\"" + Settings.DataDir + "\""); }
            catch (Exception) { }
        }

        private void Quit()
        {
            housekeeping.Stop();
            discovery.Stop();
            server.Stop();
            tray.Visible = false;
            tray.Dispose();
            pet.Close();
            ExitThread();
        }
    }
}
