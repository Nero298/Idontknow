using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using CapriHost.Ai;
using CapriHost.Core;
using CapriHost.Network;

namespace CapriHost.Ui
{
    // Cửa sổ chính của Capri Host: trạng thái, địa chỉ kết nối, các nhà cung cấp AI (thứ tự + còn/hết lượt), nhật ký trực tiếp.
    // Bấm X chỉ thu xuống khay (Capri vẫn chạy). "Thoát hẳn" mới tắt.
    internal sealed class MainForm : Form
    {
        private readonly Hub hub;
        private readonly CapriServer server;
        private readonly Label headline = new Label();
        private readonly Label address = new Label();
        private readonly ListView providers = new ListView();
        private readonly TextBox logBox = new TextBox();
        private readonly CheckBox autostart = new CheckBox();
        private readonly CheckBox petVisible = new CheckBox();
        private readonly Timer refresh = new Timer();
        private bool loading;

        internal event Action HiddenToTray;

        internal MainForm(Hub hub, CapriServer server, Action openChat, Action pair, Action quit)
        {
            this.hub = hub;
            this.server = server;
            Text = "Capri Host " + Settings.Version;
            ClientSize = new Size(560, 520);
            MinimumSize = new Size(520, 480);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);

            headline.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            headline.SetBounds(14, 12, 530, 26);

            address.SetBounds(14, 40, 530, 38);
            address.ForeColor = Color.FromArgb(60, 60, 60);

            Button chat = MakeButton("Nói chuyện với Capri", 14, 84, 150, openChat);
            Button pairButton = MakeButton("Ghép đôi điện thoại", 170, 84, 150, pair);
            Button stop = MakeButton("Dừng mọi lệnh", 326, 84, 120, delegate { hub.CancelAll("Bạn bấm Dừng trên máy tính"); });
            Button quitButton = MakeButton("Thoát hẳn", 452, 84, 94, quit);

            autostart.Text = "Khởi động cùng Windows";
            autostart.SetBounds(14, 122, 220, 24);
            autostart.CheckedChanged += delegate
            {
                if (loading) return;
                bool want = autostart.Checked;
                if (!Autostart.Set(want))
                {
                    MessageBox.Show("Không đổi được 'Khởi động cùng Windows'. Xem chi tiết trong nhật ký bên dưới.", "Capri Host", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    RefreshStatus();
                }
            };

            petVisible.Text = "Hiện Capri trên màn hình";
            petVisible.SetBounds(250, 122, 220, 24);
            petVisible.CheckedChanged += delegate
            {
                if (loading) return;
                Settings.Current.PetVisible = petVisible.Checked;
                Settings.Current.Save();
                Action changed = PetChanged;
                if (changed != null) changed();
            };

            Label aiTitle = new Label();
            aiTitle.Text = "Thứ tự AI (đổi thứ tự / bật tắt trong Cài đặt của app điện thoại):";
            aiTitle.SetBounds(14, 154, 530, 20);

            providers.View = View.Details;
            providers.FullRowSelect = true;
            providers.GridLines = true;
            providers.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            providers.Columns.Add("#", 28);
            providers.Columns.Add("Nhà cung cấp", 130);
            providers.Columns.Add("Trạng thái", 250);
            providers.Columns.Add("Lượt hôm nay", 100);
            providers.SetBounds(14, 176, 532, 138);
            providers.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            Label logTitle = new Label();
            logTitle.Text = "Nhật ký (trực tiếp):";
            logTitle.SetBounds(14, 322, 300, 20);

            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BackColor = Color.White;
            logBox.Font = new Font("Consolas", 9f);
            logBox.SetBounds(14, 344, 532, 164);
            logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            Controls.AddRange(new Control[] { headline, address, chat, pairButton, stop, quitButton, autostart, petVisible, aiTitle, providers, logTitle, logBox });

            // Nạp sẵn ~60 dòng gần nhất rồi nghe tiếp.
            foreach (object item in Log.Snapshot(60)) AppendLog(item as Dictionary<string, object>);
            Log.Pushed += OnLogPushed;

            refresh.Interval = 2000;
            refresh.Tick += delegate { if (Visible) RefreshStatus(); };
            refresh.Start();

            // Bấm X: thu xuống khay chứ không thoát.
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                    Action hidden = HiddenToTray;
                    if (hidden != null) hidden();
                }
            };
            VisibleChanged += delegate { if (Visible) RefreshStatus(); };
            RefreshStatus();
        }

        internal event Action PetChanged;

        private static Button MakeButton(string text, int x, int y, int width, Action click)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, width, 30);
            b.Click += delegate { click(); };
            return b;
        }

        // Dùng khi mở lại cửa sổ từ khay (hoặc lần mở thứ hai của exe).
        internal void BringUp()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            RefreshStatus();
        }

        internal void RefreshStatus()
        {
            loading = true;
            try
            {
                List<string> ips = Discovery.LocalAddresses();
                string first = ips.Count > 0 ? ips[0] : "(chưa thấy mạng LAN)";
                int phones = server.ClientCount;
                headline.Text = "Capri đang chạy - " + phones + " điện thoại đang kết nối";
                address.Text = "Địa chỉ nhập vào điện thoại:  " + first + "   cổng " + Settings.Current.Port +
                    (ips.Count > 1 ? "   (hoặc " + string.Join(", ", ips.GetRange(1, ips.Count - 1).ToArray()) + ")" : "");
                autostart.Checked = Autostart.IsOn();
                petVisible.Checked = Settings.Current.PetVisible;

                providers.BeginUpdate();
                providers.Items.Clear();
                int n = 1;
                foreach (object item in Agent.ProviderStatus())
                {
                    Dictionary<string, object> p = item as Dictionary<string, object>;
                    string state;
                    if (!Json.Bool(p, "configured", false)) state = Json.Str(p, "name", "") == "cloudflare" ? "đang tắt" : "chưa có khóa";
                    else if (Json.Bool(p, "off", false)) state = "đã tắt";
                    else if (Json.Int(p, "rest_sec", 0) > 0) state = "đang nghỉ " + Json.Int(p, "rest_sec", 0) + "s (hết lượt/lỗi)";
                    else state = "sẵn sàng";
                    ListViewItem row = new ListViewItem(n.ToString());
                    row.SubItems.Add(Json.Str(p, "label", ""));
                    row.SubItems.Add(state);
                    row.SubItems.Add(Json.Int(p, "today", 0).ToString());
                    if (state != "sẵn sàng") row.ForeColor = Color.Gray;
                    providers.Items.Add(row);
                    n++;
                }
                providers.EndUpdate();
            }
            finally { loading = false; }
        }

        // ---- nhật ký trực tiếp
        private void OnLogPushed(LogEntry entry)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action<LogEntry>(AppendEntry), entry); }
            catch (InvalidOperationException) { /* cửa sổ đang đóng */ }
            catch (ObjectDisposedException) { /* cửa sổ đã đóng */ }
        }

        private void AppendEntry(LogEntry entry)
        {
            AppendLog(entry.ToJson());
        }

        private void AppendLog(Dictionary<string, object> item)
        {
            if (item == null) return;
            DateTime time = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(Json.Num(item, "ts", 0)).ToLocalTime();
            StringBuilder line = new StringBuilder();
            line.Append(time.ToString("HH:mm:ss")).Append(' ').Append(Json.Str(item, "level", "").PadRight(5)).Append(' ')
                .Append(Json.Str(item, "src", "")).Append(": ").Append(Json.Str(item, "msg", "")).Append(Environment.NewLine);
            if (logBox.TextLength > 30000) logBox.Text = logBox.Text.Substring(logBox.TextLength - 15000);
            logBox.AppendText(line.ToString());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Log.Pushed -= OnLogPushed; refresh.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
