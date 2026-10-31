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
    // chạy công cụ và trả kết quả bằng CHỮ.
    //
    // QUY TẮC CỐT LÕI (v2):
    //  1. Mỗi gói có "id" LUÔN nhận đúng MỘT "reply" (thành công, lỗi, hết giờ hoặc đã dừng) - không bao giờ im lặng.
    //  2. Mỗi lệnh có một CommandContext: giờ tổng 85 giây, cờ huỷ, Abort() HTTP đang chạy.
    //  3. Quy tắc "thay thế": chat/voice mới từ CÙNG một điện thoại sẽ huỷ chat/voice cũ của điện thoại đó còn đang chạy
    //     (người dùng đã gõ câu mới thì câu cũ không còn ý nghĩa). Nút nhanh (quick) không bao giờ bị huỷ và không huỷ ai.
    //  4. "cancel {id}" / "cancel.all" huỷ ngay: Host trả lời "cancelled" tức thì và sẵn sàng nhận lệnh mới, không đợi
    //     luồng cũ chạy xong (luồng cũ dù xong sau cũng không được trả lời lần hai).
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
        private readonly Dictionary<string, CommandContext> active = new Dictionary<string, CommandContext>();
        private DateTime lastGreeting = DateTime.MinValue;
        private int localCounter;

        internal Hub()
        {
            Log.Pushed += OnLogPushed;
        }

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

        // Dòng log mới -> đẩy sang chat "Nhật ký lỗi" của mọi điện thoại đang kết nối.
        private void OnLogPushed(LogEntry entry)
        {
            CapriServer server = Server;
            if (server == null || server.ClientCount == 0) return;
            server.Broadcast(Json.D("t", "log.push", "item", entry.ToJson()));
        }

        // ---------------------------------------------------------------- kết nối

        internal void OnConnected(ClientSession session)
        {
            Log.Info("Mạng", "Điện thoại kết nối: " + session.DeviceName + " (" + session.Remote + ")", null);
            Announce(session.DeviceName + " đã kết nối", 2500);
            // Lần đầu trong ngày (hoặc sau 4 giờ) Capri chào bằng chữ.
            if (Settings.Current.Greet && (DateTime.Now - lastGreeting).TotalHours > 4)
            {
                lastGreeting = DateTime.Now;
                session.Send(Json.D("t", "reply", "id", "greet", "system", true, "text", Greeting.ForHour(DateTime.Now.Hour)));
            }
        }

        // Điện thoại mất kết nối: huỷ mọi lệnh của nó (không còn ai để trả lời) và gỡ các câu hỏi xác nhận đang chờ.
        internal void OnDisconnected(ClientSession session)
        {
            Log.Info("Mạng", "Điện thoại ngắt kết nối: " + session.DeviceName, null);
            List<CommandContext> mine = new List<CommandContext>();
            lock (active)
            {
                foreach (CommandContext c in active.Values) if (c.Owner == session) mine.Add(c);
            }
            foreach (CommandContext c in mine)
            {
                c.TryReply();       // không còn nơi để gửi
                if (c.Cancel("Điện thoại mất kết nối")) Log.Warn("Lệnh", "Huỷ lệnh vì điện thoại mất kết nối", c.Id);
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
                case "cancel":
                    CancelById(session, id);
                    break;
                case "cancel.all":
                    CancelAll("Bạn đã bấm Dừng");
                    session.Send(Json.D("t", "cancel.ok", "id", "all", "found", true));
                    break;
                case "log.get":
                    session.Send(LogPacket(Math.Max(1, Math.Min(Log.Capacity, Json.Int(message, "n", Log.Capacity))), false));
                    break;
                case "log.clear":
                    Log.Clear();
                    session.Send(LogPacket(1, true));
                    break;
                case "chat.reset":
                    Brain.Reset(Json.Str(message, "conv", "main"));
                    break;
                case "settings.get":
                    session.Send(Json.D("t", "settings", "values", Settings.Current.ToPublic()));
                    break;
                case "settings.set":
                    Settings.Current.Apply(Json.Obj(message, "values"));
                    Agent.ResetRest();          // đổi khóa/thứ tự: cho mọi nhà cung cấp thử lại ngay
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
                    session.Send(Json.D("t", "status", "host", Environment.MachineName, "version", Settings.Version, "v", Settings.ProtocolVersion,
                        "gemini", Settings.Current.GeminiKey.Length > 0, "nvidia", Settings.Current.NvidiaKey.Length > 0,
                        "providers", Agent.ProviderStatus(), "busy", ActiveCount()));
                    break;
                default:
                    // Gói lạ (bản Android mới hơn Host): nếu có id thì báo rõ thay vì im lặng.
                    if (id.Length > 0)
                        session.Send(Json.D("t", "reply", "id", id, "error", true,
                            "text", "Host (bản " + Settings.Version + ") chưa hiểu lệnh '" + type + "'. Hãy cập nhật Host lên bản mới nhất."));
                    Log.Warn("Lệnh", "Gói không nhận ra: '" + type + "'", id);
                    break;
            }
        }

        private Dictionary<string, object> LogPacket(int n, bool cleared)
        {
            Settings s = Settings.Current;
            return Json.D("t", "log", "items", Log.Snapshot(n), "cleared", cleared,
                "host", Environment.MachineName, "version", Settings.Version, "v", Settings.ProtocolVersion,
                "os", Environment.OSVersion.VersionString, "now", (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds,
                "order", s.ProviderOrder, "off", s.ProviderOff,
                "models", Json.D("gemini", s.GeminiModel, "groq", s.GroqModel, "literouter", s.LiteRouterModel,
                                 "openrouter", s.OpenRouterModel, "nvidia", s.NvidiaModel),
                "providers", Agent.ProviderStatus(), "browser", s.Browser);
        }

        // ---------------------------------------------------------------- quản lý lệnh đang chạy

        private int ActiveCount()
        {
            lock (active) { return active.Count; }
        }

        private CommandContext Begin(ClientSession session, string id, string conv, string kind)
        {
            CommandContext cmd = new CommandContext(id, conv, kind, session);
            List<CommandContext> superseded = new List<CommandContext>();
            lock (active)
            {
                if (kind != "quick")
                {
                    foreach (CommandContext old in active.Values)
                        if (old.Owner == session && old.Kind != "quick" && !old.Cancelled) superseded.Add(old);
                }
                active[id] = cmd;
            }
            foreach (CommandContext old in superseded)
            {
                if (old.Cancel("Bị thay bằng lệnh mới"))
                {
                    Log.Info("Lệnh", "Lệnh cũ bị thay bằng lệnh mới '" + id + "'", old.Id);
                    Finish(session, old, "Đã dừng vì bạn gửi lệnh mới.", false, true, null);
                }
            }
            cmd.Arm(Timeouts.CommandMs);
            return cmd;
        }

        private void End(CommandContext cmd)
        {
            cmd.Disarm();
            lock (active)
            {
                CommandContext now;
                if (active.TryGetValue(cmd.Id, out now) && now == cmd) active.Remove(cmd.Id);
            }
        }

        // Trả lời đúng một lần. extras có thể null.
        private void Finish(ClientSession session, CommandContext cmd, string text, bool error, bool cancelled, Dictionary<string, object> extras)
        {
            if (cmd != null && !cmd.TryReply()) return;
            Dictionary<string, object> reply = Json.D("t", "reply", "id", cmd == null ? "" : cmd.Id, "text", text, "error", error);
            if (cancelled) reply["cancelled"] = true;
            if (cmd != null)
            {
                reply["summary"] = cmd.Summary();
                reply["ms"] = cmd.Clock.ElapsedMilliseconds;
            }
            if (extras != null) foreach (KeyValuePair<string, object> pair in extras) reply[pair.Key] = pair.Value;
            if (session != null) session.Send(reply);
            if (cmd != null) Log.Info("Lệnh", (error ? "LỖI - " : cancelled ? "Đã dừng - " : "Xong - ") + cmd.Summary(), cmd.Id);
        }

        private void CancelById(ClientSession session, string id)
        {
            CommandContext cmd;
            lock (active) active.TryGetValue(id, out cmd);
            bool found = cmd != null && !cmd.Replied;
            session.Send(Json.D("t", "cancel.ok", "id", id, "found", found));
            if (!found) return;
            if (cmd.Cancel("Bạn đã bấm Dừng"))
            {
                Log.Info("Lệnh", "Người dùng bấm Dừng", id);
                Announce("Đã dừng", 1800);
                Finish((ClientSession)cmd.Owner, cmd, "Đã dừng.", false, true, null);
            }
        }

        // Dừng mọi lệnh (nút trên khay hệ thống, nút Dừng ở chat trên PC, cancel.all).
        internal void CancelAll(string why)
        {
            List<CommandContext> all;
            lock (active) all = new List<CommandContext>(active.Values);
            int n = 0;
            foreach (CommandContext cmd in all)
            {
                if (cmd.Cancel(why))
                {
                    n++;
                    Finish(cmd.Owner as ClientSession, cmd, "Đã dừng.", false, true, null);
                }
            }
            if (n > 0)
            {
                Log.Info("Lệnh", "Đã dừng " + n + " lệnh (" + why + ")", null);
                Announce("Đã dừng", 1800);
            }
            // Gỡ luôn mọi câu hỏi xác nhận đang chờ.
            lock (pending)
            {
                foreach (Pending p in pending.Values)
                {
                    p.Answer = false;
                    try { p.Done.Set(); }
                    catch (ObjectDisposedException) { /* đã giải phóng */ }
                }
            }
        }

        // ---------------------------------------------------------------- chạy một lệnh (khung chung, luôn trả lời)

        private delegate void CommandBody(CommandContext cmd);

        private void Execute(ClientSession session, string id, string conv, string kind, CommandBody body)
        {
            if (id.Length == 0) id = "x" + Guid.NewGuid().ToString("N").Substring(0, 8);
            CommandContext cmd = Begin(session, id, conv, kind);
            try
            {
                body(cmd);
            }
            catch (CancelledException)
            {
                HandleCancelled(session, cmd);
            }
            catch (AiException error)
            {
                Log.Add("ERROR", "AI", error.Message, error.Body, id);
                Finish(session, cmd, error.Message, true, false, null);
                Announce("Có lỗi, xem trên điện thoại", 2500);
            }
            catch (Exception error)
            {
                Log.Error("Lệnh", "Lệnh '" + kind + "' gặp lỗi bất ngờ", error, id);
                Finish(session, cmd, "Host gặp lỗi: " + error.GetType().Name + " - " + error.Message + ". Xem chi tiết trong chat 'Nhật ký lỗi'.", true, false, null);
                Announce("Có lỗi, xem trên điện thoại", 2500);
            }
            finally
            {
                // Chốt cuối: nếu vì lý do gì lệnh chưa được trả lời thì trả lời ngay (không để điện thoại chờ vô hạn).
                if (!cmd.Replied) Finish(session, cmd, "Lệnh kết thúc mà không có kết quả. Xem chat 'Nhật ký lỗi'.", true, false, null);
                End(cmd);
            }
        }

        private void HandleCancelled(ClientSession session, CommandContext cmd)
        {
            if (cmd.TimedOut)
            {
                string text = "Hết thời gian chờ (" + (Timeouts.CommandMs / 1000) + " giây) mà chưa xong. " + cmd.Summary() + ".";
                Log.Add("ERROR", "Lệnh", text, null, cmd.Id);
                Finish(session, cmd, text, true, false, Json.D("timeout", true));
                Announce("Quá thời gian, xem điện thoại", 2500);
            }
            else
            {
                Finish(session, cmd, "Đã dừng.", false, true, null);     // nếu đã trả lời lúc huỷ thì Finish tự bỏ qua
            }
        }

        private ToolContext NewContext(ClientSession session, CommandContext cmd)
        {
            ToolContext ctx = new ToolContext();
            ctx.Cmd = cmd;
            if (session != null) ctx.Confirm = delegate(string question) { return AskPhone(session, cmd, question); };
            else ctx.Confirm = delegate(string question) { return AskLocalConfirm(cmd, question); };
            ctx.Progress = delegate(string step) { Announce(step, 3500); };
            return ctx;
        }

        private void Chat(ClientSession session, string id, string conv, string text)
        {
            text = (text ?? "").Trim();
            if (id.Length == 0) return;
            if (text.Length == 0)
            {
                session.Send(Json.D("t", "reply", "id", id, "error", true, "text", "Tin nhắn trống."));
                return;
            }
            Execute(session, id, conv, "chat", delegate(CommandContext cmd)
            {
                Announce("Nhận lệnh: " + Tools.Shorten(text, 46), 2000);
                Log.Info("Lệnh", "Chat: " + Tools.Shorten(text, 80), id);
                session.Send(Json.D("t", "thinking", "id", id));
                Respond(session, cmd, conv, text);
            });
        }

        // Tin nhắn thoại từ điện thoại: Gemini chuyển thành chữ, sau đó đi như lệnh gõ bình thường.
        private void VoiceToText(ClientSession session, string id, string conv, string data)
        {
            if (id.Length == 0) return;
            Execute(session, id, conv, "voice", delegate(CommandContext cmd)
            {
                session.Send(Json.D("t", "thinking", "id", id));
                string heard;
                byte[] wav;
                try { wav = Convert.FromBase64String(data); }
                catch (FormatException)
                {
                    Log.Add("ERROR", "Lệnh", "Âm thanh gửi lên bị lỗi (base64)", null, id);
                    Finish(session, cmd, "Âm thanh gửi lên bị lỗi.", true, false, null);
                    return;
                }
                heard = Brain.Transcribe(wav, cmd);
                if (heard.Length == 0 || heard.IndexOf("[silence]", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Finish(session, cmd, "Mình không nghe thấy gì cả. Thử nói gần micro hơn nhé.", true, false, null);
                    return;
                }
                session.Send(Json.D("t", "transcript", "id", id, "text", heard));
                Announce("Nhận lệnh: " + Tools.Shorten(heard, 46), 2000);
                Log.Info("Lệnh", "Giọng nói: " + Tools.Shorten(heard, 80), id);
                Respond(session, cmd, conv, heard);
            });
        }

        // Nút nhanh: chạy thẳng công cụ, không qua AI (nhanh và không tốn hạn mức API).
        private void Quick(ClientSession session, string id, string tool, Dictionary<string, object> args)
        {
            if (id.Length == 0) return;
            Execute(session, id, "quick", "quick", delegate(CommandContext cmd)
            {
                ToolContext ctx = new ToolContext();
                ctx.UserInitiated = true;
                ctx.Cmd = cmd;
                Announce(Tools.Describe(tool, args), 3000);
                Log.Info("Lệnh", "Nút nhanh: " + tool, id);
                ToolOutcome outcome = Tools.RunTimed(tool, args, ctx, cmd);
                cmd.AddTool(cmd.Clock.ElapsedMilliseconds);
                Announce(outcome.Text, 2200);
                if (!outcome.Ok) Log.Add("ERROR", "Công cụ", tool + " thất bại: " + outcome.Text, null, id);
                Dictionary<string, object> extras = Json.D("quick", true);
                if (outcome.Image != null) extras["image"] = Convert.ToBase64String(outcome.Image);
                Finish(session, cmd, outcome.Text, !outcome.Ok, false, extras);
            });
        }

        private void Respond(ClientSession session, CommandContext cmd, string conv, string text)
        {
            AgentResult result = Brain.Chat(conv, text, NewContext(session, cmd), cmd);
            cmd.ThrowIfCancelled();

            Dictionary<string, object> extras = new Dictionary<string, object>();
            List<object> notes = new List<object>();
            foreach (ActionNote note in result.Notes) notes.Add(Json.D("tool", note.Tool, "ok", note.Ok, "text", note.Text));
            extras["notes"] = notes;
            if (result.Image != null) extras["image"] = Convert.ToBase64String(result.Image);
            if (result.Provider.Length > 0) extras["provider"] = result.Provider;
            if (result.Switched.Length > 0) extras["switched"] = result.Switched;

            if (result.Error != null) Log.Add("ERROR", "AI", result.Error, null, cmd.Id);
            Finish(session, cmd, result.Error != null ? result.Error : result.Text, result.Error != null, false, extras);

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
            internal bool Cancelled;
            internal string Summary;
            internal List<ActionNote> Notes;
        }

        internal LocalReply AskLocal(string text, byte[] voiceWav, Action<string> heardCallback)
        {
            LocalReply reply = new LocalReply();
            reply.Notes = new List<ActionNote>();
            reply.Summary = "";
            string id = "local-" + Interlocked.Increment(ref localCounter);
            CommandContext cmd = Begin(null, id, "local", "chat");
            try
            {
                if (voiceWav != null)
                {
                    text = Brain.Transcribe(voiceWav, cmd);
                    if (text.Length == 0 || text.IndexOf("[silence]", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        reply.Text = "Mình không nghe thấy gì cả.";
                        reply.Error = true;
                        return reply;
                    }
                    if (heardCallback != null) heardCallback(text);
                }
                Announce("Nhận lệnh: " + Tools.Shorten(text, 46), 2000);
                Log.Info("Lệnh", "Chat trên PC: " + Tools.Shorten(text, 80), id);
                AgentResult result = Brain.Chat("local", text, NewContext(null, cmd), cmd);
                cmd.ThrowIfCancelled();
                reply.Text = result.Error != null ? result.Error : result.Text;
                reply.Error = result.Error != null;
                reply.Notes = result.Notes;
                if (result.Error != null) Log.Add("ERROR", "AI", result.Error, null, id);
                else RaiseSaid(result.Text);
            }
            catch (CancelledException)
            {
                if (cmd.TimedOut)
                {
                    reply.Text = "Hết thời gian chờ (" + (Timeouts.CommandMs / 1000) + " giây) mà chưa xong.";
                    reply.Error = true;
                    Log.Add("ERROR", "Lệnh", reply.Text, null, id);
                }
                else { reply.Text = "Đã dừng."; reply.Cancelled = true; }
            }
            catch (AiException error)
            {
                Log.Add("ERROR", "AI", error.Message, error.Body, id);
                reply.Text = error.Message;
                reply.Error = true;
            }
            catch (Exception error)
            {
                Log.Error("Lệnh", "Chat trên PC gặp lỗi bất ngờ", error, id);
                reply.Text = "Host gặp lỗi: " + error.GetType().Name + " - " + error.Message;
                reply.Error = true;
            }
            finally
            {
                reply.Summary = cmd.Summary();
                cmd.TryReply();
                End(cmd);
            }
            return reply;
        }

        // ---------------------------------------------------------------- danh sách mô hình

        private void Models(ClientSession session, string id, string provider, string key)
        {
            IProvider p = Agent.ByName(provider);
            if (p == null)
            {
                session.Send(Json.D("t", "models", "id", id, "provider", provider, "ok", false, "msg", "Nhà cung cấp '" + provider + "' không có danh sách mô hình.", "items", new List<object>()));
                return;
            }
            try
            {
                List<string> items = p.ListModels(key);
                List<object> list = new List<object>();
                foreach (string item in items) list.Add(item);
                session.Send(Json.D("t", "models", "id", id, "provider", provider, "ok", true, "items", list));
            }
            catch (AiException error)
            {
                Log.Warn("AI", "Không tải được danh sách mô hình " + p.Label + ": " + error.Message, null);
                session.Send(Json.D("t", "models", "id", id, "provider", provider, "ok", false, "msg", error.Message, "items", new List<object>()));
            }
            catch (Exception error)
            {
                Log.Error("AI", "Lỗi tải danh sách mô hình " + p.Label, error, null);
                session.Send(Json.D("t", "models", "id", id, "provider", provider, "ok", false, "msg", error.GetType().Name + ": " + error.Message, "items", new List<object>()));
            }
        }

        // ---------------------------------------------------------------- xác nhận (điện thoại hoặc hộp thoại trên PC)

        // Hỏi điện thoại. Hết 60 giây, bị Dừng, hoặc điện thoại mất kết nối = trả lời "Không". Thời gian chờ không tính vào hạn lệnh.
        private bool AskPhone(ClientSession session, CommandContext cmd, string question)
        {
            if (!session.Alive) return false;
            string cid = Guid.NewGuid().ToString("N");
            Pending wait = new Pending();
            lock (pending) pending[cid] = wait;
            cmd.BeginPause();
            try
            {
                Announce("Đợi bạn xác nhận trên điện thoại", 4000);
                if (!session.Send(Json.D("t", "confirm", "cid", cid, "text", question, "cmd", cmd.Id))) return false;
                int which = WaitHandle.WaitAny(new WaitHandle[] { wait.Done, cmd.CancelHandle }, Timeouts.ConfirmMs);
                bool answer;
                lock (pending) answer = which == 0 && wait.Answer;
                if (which == WaitHandle.WaitTimeout)
                {
                    Log.Warn("Lệnh", "Không ai xác nhận trong " + (Timeouts.ConfirmMs / 1000) + " giây -> coi như 'Không': " + Tools.Shorten(question, 70), cmd.Id);
                    session.Send(Json.D("t", "confirm.expire", "cid", cid));
                }
                return answer;
            }
            finally
            {
                cmd.EndPause();
                cmd.Rearm();
                lock (pending) pending.Remove(cid);
                // Cố ý KHÔNG Dispose/Close wait.Done: một Resolve muộn có thể vẫn cầm tham chiếu (tránh ObjectDisposedException).
            }
        }

        private bool AskLocalConfirm(CommandContext cmd, string question)
        {
            Func<string, bool> ask = LocalConfirm;
            if (ask == null) return false;
            cmd.BeginPause();
            try { return ask(question); }
            finally { cmd.EndPause(); cmd.Rearm(); }
        }

        private void Resolve(string cid, bool ok)
        {
            lock (pending)
            {
                Pending wait;
                if (!pending.TryGetValue(cid, out wait)) return;
                wait.Answer = ok;
                wait.Done.Set();
            }
        }
    }
}
