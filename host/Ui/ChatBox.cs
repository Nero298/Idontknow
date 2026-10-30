using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using CapriHost.Ai;
using CapriHost.Network;
using CapriHost.Speech;

namespace CapriHost.Ui
{
    // Cửa sổ chat ngay trên PC: gõ chữ hoặc bấm Mic để nói (cần khóa Gemini để nghe giọng nói).
    internal sealed class ChatBox : Form
    {
        private readonly Hub hub;
        private readonly TextBox log = new TextBox();
        private readonly TextBox input = new TextBox();
        private readonly Button send = new Button();
        private readonly Button mic = new Button();
        private readonly Label status = new Label();
        private readonly Mic recorder = new Mic();
        private bool busy;

        internal ChatBox(Hub hub)
        {
            this.hub = hub;
            Text = "Nói chuyện với Capri";
            ClientSize = new Size(420, 360);
            MinimumSize = new Size(320, 260);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            ShowInTaskbar = true;

            log.Multiline = true;
            log.ReadOnly = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.BackColor = Color.White;
            log.Dock = DockStyle.Fill;

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 62;
            bottom.Padding = new Padding(6);

            status.Dock = DockStyle.Top;
            status.Height = 16;
            status.ForeColor = Color.Gray;
            status.Text = "";

            mic.Text = "Mic";
            mic.Width = 48;
            mic.Dock = DockStyle.Right;
            mic.Click += delegate { ToggleMic(); };

            send.Text = "Gửi";
            send.Width = 56;
            send.Dock = DockStyle.Right;
            send.Click += delegate { SendText(); };

            input.Dock = DockStyle.Fill;
            input.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendText(); }
            };

            bottom.Controls.Add(input);
            bottom.Controls.Add(mic);
            bottom.Controls.Add(send);
            bottom.Controls.Add(status);
            Controls.Add(log);
            Controls.Add(bottom);

            // Bấm X chỉ ẩn cửa sổ để lần sau mở lại nhanh và giữ lịch sử.
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); if (recorder.Recording) recorder.Stop(); }
            };
        }

        internal void Open()
        {
            if (!Visible) Show();
            WindowState = FormWindowState.Normal;
            Activate();
            input.Focus();
        }

        private void Append(string who, string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string, string>(Append), who, text); return; }
            log.AppendText(who + ": " + text + Environment.NewLine + Environment.NewLine);
        }

        private void SetBusy(bool value, string message)
        {
            if (InvokeRequired) { BeginInvoke(new Action<bool, string>(SetBusy), value, message); return; }
            busy = value;
            send.Enabled = !value;
            status.Text = message;
        }

        private void SendText()
        {
            string text = input.Text.Trim();
            if (text.Length == 0 || busy) return;
            input.Clear();
            Append("Bạn", text);
            Ask(text, null);
        }

        private void ToggleMic()
        {
            if (busy) return;
            try
            {
                if (!recorder.Recording)
                {
                    recorder.Start();
                    mic.Text = "Dừng";
                    status.Text = "Đang nghe... bấm lần nữa để gửi";
                    return;
                }
                byte[] wav = recorder.Stop();
                mic.Text = "Mic";
                status.Text = "";
                if (wav == null) { Append("Capri", "Mình không thu được âm thanh nào."); return; }
                Ask(null, wav);
            }
            catch (InvalidOperationException error)
            {
                mic.Text = "Mic";
                Append("Capri", error.Message);
            }
        }

        // Chạy trên luồng nền để giao diện không bị đơ khi chờ AI.
        private void Ask(string text, byte[] wav)
        {
            SetBusy(true, "Capri đang nghĩ...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                Hub.LocalReply reply = hub.AskLocal(text, wav, delegate(string heard) { Append("Bạn (giọng nói)", heard); });
                foreach (ActionNote note in reply.Notes) Append("  ›", (note.Ok ? "✓ " : "✗ ") + note.Text);
                Append("Capri", reply.Text);
                SetBusy(false, "");
            });
        }
    }
}
