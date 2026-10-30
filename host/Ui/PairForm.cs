using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CapriHost.Core;
using CapriHost.Network;

namespace CapriHost.Ui
{
    // Hiện mã ghép đôi và địa chỉ kết nối để nhập vào app Android.
    internal sealed class PairForm : Form
    {
        private readonly Label code = new Label();
        private readonly Label address = new Label();
        private readonly Label info = new Label();
        private readonly int port;

        internal PairForm(int port)
        {
            this.port = port;
            Text = "Ghép đôi điện thoại";
            ClientSize = new Size(380, 250);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);

            Label step = new Label();
            step.Text = "Mở Capri trên điện thoại (cùng WiFi) và nhập mã này:";
            step.SetBounds(16, 14, 350, 22);

            code.Font = new Font("Consolas", 26f, FontStyle.Bold);
            code.ForeColor = Color.FromArgb(201, 100, 66);
            code.TextAlign = ContentAlignment.MiddleCenter;
            code.SetBounds(16, 40, 348, 56);

            address.TextAlign = ContentAlignment.MiddleCenter;
            address.SetBounds(16, 100, 348, 40);

            info.ForeColor = Color.Gray;
            info.SetBounds(16, 146, 348, 40);

            Button renew = new Button();
            renew.Text = "Tạo mã mới";
            renew.SetBounds(16, 200, 110, 30);
            renew.Click += delegate { Pairing.Start(); Refresh_(); };

            Button close = new Button();
            close.Text = "Đóng";
            close.SetBounds(254, 200, 110, 30);
            close.Click += delegate { Close(); };

            Controls.AddRange(new Control[] { step, code, address, info, renew, close });
            Pairing.Paired += OnPaired;
            FormClosed += delegate { Pairing.Paired -= OnPaired; };
            if (!Pairing.Active) Pairing.Start();
            Refresh_();
        }

        private void Refresh_()
        {
            code.Text = Pairing.Code ?? "-----";
            List<string> ips = Discovery.LocalAddresses();
            address.Text = ips.Count == 0 ? "Chưa thấy mạng LAN" : "Địa chỉ máy này:  " + string.Join("  hoặc  ", ips.ToArray()) + "\nCổng: " + port;
            info.Text = "Mã có hiệu lực đến " + Pairing.ExpiresAtLocal.ToString("HH:mm") + ". Trên điện thoại có thể bấm \"Tìm Capri trong mạng\" để khỏi nhập địa chỉ.";
        }

        private void OnPaired(string deviceName)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(OnPaired), deviceName); return; }
            code.Text = "✓";
            info.Text = "Đã ghép đôi với " + deviceName + ".";
            Timer t = new Timer();
            t.Interval = 1800;
            t.Tick += delegate { t.Stop(); t.Dispose(); Close(); };
            t.Start();
        }
    }
}
