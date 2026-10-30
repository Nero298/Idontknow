using System;
using System.Collections.Generic;
using System.Threading;
using CapriHost.Actions;
using CapriHost.Ai;
using CapriHost.Core;
using CapriHost.Speech;

namespace CapriHost.Network
{
    // Nơi gặp nhau của mọi thứ: nhận lệnh từ điện thoại (hoặc cửa sổ chat trên PC), gọi Agent (AI),
    // chạy action và trả kết quả bằng CHỮ. Chưa có giọng nói: mọi phản hồi đều là text.
    //
    // Luồng chính:  điện thoại gõ lệnh -> Hub "bắt" lệnh (Activity: "Nhận lệnh...") -> AI chọn công cụ
    //   -> mỗi công cụ báo tiến độ (Activity: "Đang mở YouTube...") -> trả kết quả về điện thoại.
    // Sprite Capri trên màn hình hiện bong bóng nhỏ theo từng Activity rồi tự biến mất.
    internal sealed class Hub
    {
        internal readonly Agent Brain = new Agent();
        internal CapriServer Server;

        // Bong bóng hoạt động cạnh sprite: (nội dung, thời gian giữ tính bằng mili giây).
        internal event Action<string, int> Activity;
        // Capri nói một câu trả lời (chỉ dùng cho chat ngay trên PC và lời chào).
        internal event Action<string> Said;
        // Cần hỏi xác nhận ngay trên PC (hộp thoại). Do giao diện gắn vào.
        internal Func<string, bool> LocalConfirm;
        internal event Action SettingsChanged;

        private sealed class Pending
        {
            internal readonly ManualResetEvent Done = new ManualResetEvent(false);
            internal bool Answer;
        }

        private readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
        private DateTime lastGreeting = DateTime.MinValue;

        private void Announce(string text, int holdMs)
        {
            Action<string, int> handler = Activity;
            if (handler != null && !string.IsNullOrEmpty(text)) handler(text, holdMs);
        }

        private void RaiseSaid(string text)
        {
            Action<string> handler = Said;
            if (handler != null && !string.IsNullOrEmpty(text)) handler(text);
        }

        // ---------------------------------------------------------------- kết nối

        internal void OnConnected(ClientSession session)
        {
            Log.Info("Điện thoại kết nối: " + session.DeviceName + " (" + session.Remote + ")");
            Announce(session.DeviceName + " đã kết nối", 2500);
            // Lần đầu trong ngày (hoặc sau 4 giờ) Capri chào bằng chữ.
            if (Settings.Current.Greet && (DateTime.Now - lastGreeting).TotalHours > 4)
            {
                lastGreeting = DateTime.Now;
                session.Send(Json.D("t", "reply", "id", "greet", "system", true, "text", Greeting.ForHour(DateTime.Now.Hour)));
            }
        }

        // Chào bằng bong bóng chữ trên PC lúc khởi động.
        internal void GreetLocal()
        {
            if (!Settings.Current.Greet) return;
            lastGreeting = DateTime.Now;
            RaiseSaid(Greeting.ForHour(DateTime.Now.Hour));
        }

        // ---------------------------------------------------------------- gói tin từ điện thoại

        internal void Handle(ClientSession session, Dictionary<string, object> message)
        {
            string type = Json.Str(message, "t", "");
            string id = Json.Str(message, "id", "");
            switch (type)
            {
                case "chat":
                    Chat(session, id, Json.Str(message, "conv", "main"), Json.Str(message, "text", ""));
                    break;
                case "voice":
                    VoiceToText(session, id, Json.Str(message, "conv", "main"), Json.Str(message, "data", ""));
                    break;
                case "quick":
                    Quick(session, id, Json.Str(message, "tool", ""), Json.Obj(message, "args"));
                    break;
                case "chat.reset":
                    Brain.Reset(Json.Str(message, "conv", "main"));
                    break;
                case "settings.get":
                    session.Send(Json.D("t", "settings", "values", Settings.Current.ToPublic()));
                    break;
                case "settings.set":
                    Settings.Current.Apply(Json.Obj(message, "values"));
                    session.Send(Json.D("t", "settings", "id", id, "values", Settings.Current.ToPublic()));
                    Action changed = SettingsChanged;
                    if (changed != null) changed();
                    break;
                case "models.list":
                    Models(session, id, Json.Str(message, "provider", "gemini"), Json.Str(message, "key", ""));
                    break;
                case "confirm.reply":
                    Resolve(Json.Str(message, "cid", ""), Json.Bool(message, "ok", false));
                    break;
                case "status.get":
                    session.Send(Json.D("t", "status", "host", Environment.MachineName, "version", Settings.Version,
                        "gemini", Settings.Current.GeminiKey.Length > 0, "nvidia", Settings.Current.NvidiaKey.Length > 0));
                    break;
            }
        }

        private ToolContext NewContext(ClientSession session)
        {
            ToolContext ctx = new ToolContext();
            if (session != null) ctx.Confirm = delegate(string question) { return AskPhone(session, question); };
            else ctx.Confirm = LocalConfirm;
            ctx.Progress = delegate(string step) { Announce(step, 3500); };
            return ctx;
        }

        private void Chat(ClientSession session, string id, string conv, string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            Announce("Nhận lệnh: " + Tools.Shorten(text, 46), 2000);
            session.Send(Json.D("t", "thinking", "id", id));
            Respond(session, id, conv, text);
        }

        // Tin nhắn thoại từ điện thoại: Gemini chuyển thành chữ, sau đó đi như lệnh gõ bình thường.
        private void VoiceToText(ClientSession session, string id, string conv, string data)
        {
            session.Send(Json.D("t", "thinking", "id", id));
            string heard;
            try { heard = Brain.Transcribe(Convert.FromBase64String(data)); }
            catch (AiException error) { session.Send(Json.D("t", "reply", "id", id, "error", true, "text", error.Message)); return; }
            catch (FormatException) { session.Send(Json.D("t", "reply", "id", id, "error", true, "text", "Âm thanh gửi lên bị lỗi.")); return; }
            if (heard.Length == 0 || heard.IndexOf("[silence]", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                session.Send(Json.D("t", "reply", "id", id, "error", true, "text", "Mình không nghe thấy gì cả. Thử nói gần micro hơn nhé."));
                return;
            }
            session.Send(Json.D("t", "transcript", "id", id, "text", heard));
            Announce("Nhận lệnh: " + Tools.Shorten(heard, 46), 2000);
            Respond(session, id, conv, heard);
        }

        // Nút nhanh: chạy thẳng công cụ, không qua AI (nhanh và không tốn hạn mức API).
        private void Quick(ClientSession session, string id, string tool, Dictionary<string, object> args)
        {
            ToolContext ctx = new ToolContext();
            ctx.UserInitiated = true;
            Announce(Tools.Describe(tool, args), 3000);
            ToolOutcome outcome = Tools.Run(tool, args, ctx);
            Announce(outcome.Text, 2200);
            Dictionary<string, object> reply = Json.D("t", "reply", "id", id, "text", outcome.Text, "error", !outcome.Ok, "quick", true);
            if (outcome.Image != null) reply["image"] = Convert.ToBase64String(outcome.Image);
            session.Send(reply);
        }

        private void Respond(ClientSession session, string id, string conv, string text)
        {
            AgentResult result = Brain.Chat(conv, text, NewContext(session));

            Dictionary<string, object> reply = Json.D("t", "reply", "id", id, "text", result.Error != null ? result.Error : result.Text, "error", result.Error != null);
            List<object> notes = new List<object>();
            foreach (ActionNote note in result.Notes) notes.Add(Json.D("tool", note.Tool, "ok", note.Ok, "text", note.Text));
            reply["notes"] = notes;
            if (result.Image != null) reply["image"] = Convert.ToBase64String(result.Image);
            session.Send(reply);

            // Bong bóng kết thúc ngắn gọn rồi tự tắt.
            if (result.Error != null) Announce("Có lỗi, xem trên điện thoại", 2500);
            else if (result.Notes.Count > 0)
            {
                ActionNote last = result.Notes[result.Notes.Count - 1];
                Announce(last.Ok ? "Xong rồi" : Tools.Shorten(last.Text, 60), 2000);
            }
        }

        // ---------------------------------------------------------------- cửa sổ chat trên PC (gõ chữ hoặc nói qua Mic)

        internal struct LocalReply
        {
            internal string Text;
            internal bool Error;
            internal List<ActionNote> Notes;
        }

        internal LocalReply AskLocal(string text, byte[] voiceWav, Action<string> heardCallback)
        {
            LocalReply reply = new LocalReply();
            reply.Notes = new List<ActionNote>();
            try
            {
                if (voiceWav != null)
                {
                    text = Brain.Transcribe(voiceWav);
                    if (text.Length == 0 || text.IndexOf("[silence]", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        reply.Text = "Mình không nghe thấy gì cả.";
                        reply.Error = true;
                        return reply;
                    }
                    if (heardCallback != null) heardCallback(text);
                }
            }
            catch (AiException error) { reply.Text = error.Message; reply.Error = true; return reply; }

            Announce("Nhận lệnh: " + Tools.Shorten(text, 46), 2000);
            AgentResult result = Brain.Chat("local", text, NewContext(null));
            reply.Text = result.Error != null ? result.Error : result.Text;
            reply.Error = result.Error != null;
            reply.Notes = result.Notes;
            if (result.Error == null) RaiseSaid(result.Text);
            return reply;
        }

        // ---------------------------------------------------------------- danh sách mô hình

        private void Models(ClientSession session, string id, string provider, string key)
        {
            try
            {
                IProvider p = provider == "nvidia" ? (IProvider)Agent.Nvidia : Agent.Gemini;
                List<string> items = p.ListModels(key);
                List<object> list = new List<object>();
                foreach (string item in items) list.Add(item);
                session.Send(Json.D("t", "models", "id", id, "provider", provider, "ok", true, "items", list));
            }
            catch (AiException error)
            {
                session.Send(Json.D("t", "models", "id", id, "provider", provider, "ok", false, "msg", error.Message, "items", new List<object>()));
            }
        }

        // ---------------------------------------------------------------- xác nhận trên điện thoại

        private bool AskPhone(ClientSession session, string question)
        {
            if (!session.Alive) return false;
            string cid = Guid.NewGuid().ToString("N");
            Pending wait = new Pending();
            lock (pending) pending[cid] = wait;
            try
            {
                Announce("Đợi bạn xác nhận trên điện thoại", 4000);
                if (!session.Send(Json.D("t", "confirm", "cid", cid, "text", question))) return false;
                return wait.Done.WaitOne(60000) && wait.Answer;
            }
            finally
            {
                lock (pending) pending.Remove(cid);
                wait.Done.Close();
            }
        }

        private void Resolve(string cid, bool ok)
        {
            Pending wait;
            lock (pending) pending.TryGetValue(cid, out wait);
            if (wait == null) return;
            wait.Answer = ok;
            try { wait.Done.Set(); }
            catch (ObjectDisposedException) { }
        }
    }
}
