using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using CapriHost.Actions;
using CapriHost.Core;

namespace CapriHost.Ai
{
    internal sealed class ActionNote
    {
        internal string Tool;
        internal bool Ok;
        internal string Text;
    }

    internal sealed class AgentResult
    {
        internal string Text = "";
        internal string Error;                       // khác null nếu AI lỗi
        internal string Provider = "";               // nhà cung cấp đã trả lời
        internal string Switched = "";               // khác rỗng nếu đã phải chuyển nhà cung cấp ("Gemini hết lượt -> Groq")
        internal byte[] Image;                       // ảnh chụp màn hình (nếu có)
        internal readonly List<ActionNote> Notes = new List<ActionNote>();
    }

    // "Bộ não": nhận câu nói, hỏi LLM theo THỨ TỰ ưu tiên (Gemini -> Cloudflare Worker -> Groq -> OpenRouter -> NVIDIA, người dùng đổi được),
    // chạy công cụ AI yêu cầu, lặp đến khi có câu trả lời cuối. Nhà cung cấp nào hết hạn mức (429) bị "nghỉ" một lúc rồi tự dùng lại.
    // Lịch sử hội thoại giữ trong RAM, tối đa 24 tin mỗi cuộc trò chuyện.
    internal sealed class Agent
    {
        internal static readonly GeminiProvider Gemini = new GeminiProvider();
        internal static readonly CloudflareProvider Cloudflare = new CloudflareProvider();
        internal static readonly GroqProvider Groq = new GroqProvider();
        internal static readonly LiteRouterProvider LiteRouter = new LiteRouterProvider();
        internal static readonly OpenRouterProvider OpenRouter = new OpenRouterProvider();
        internal static readonly NvidiaProvider Nvidia = new NvidiaProvider();

        private const int MaxHistory = 24;
        private const int MaxRounds = 6;

        // Thời gian nghỉ (giây) theo loại lỗi.
        private const int RestRateLimitSec = 60;        // 429 thường (theo phút)
        private const int RestDailySec = 1800;          // 429 theo ngày: dò lại mỗi 30 phút (tự dùng lại sau khi reset)
        private const int RestBadKeySec = 600;          // sai khóa / hết tiền / không có mô hình: chờ người dùng sửa
        private const int RestTransientSec = 30;        // lỗi mạng / 5xx

        private sealed class Rest
        {
            internal DateTime Until;
            internal string Why = "";
        }

        private sealed class Counter
        {
            internal string Day = "";
            internal int Count;
        }

        private static readonly object StateGate = new object();
        private static readonly Dictionary<string, Rest> Resting = new Dictionary<string, Rest>();
        private static readonly Dictionary<string, Counter> Counters = new Dictionary<string, Counter>();

        private readonly Dictionary<string, List<ChatMsg>> history = new Dictionary<string, List<ChatMsg>>();

        internal static IProvider ByName(string name)
        {
            switch (name)
            {
                case "gemini": return Gemini;
                case "cloudflare": return Cloudflare;
                case "groq": return Groq;
                case "literouter": return LiteRouter;
                case "openrouter": return OpenRouter;
                case "nvidia": return Nvidia;
            }
            return null;
        }

        // ---- Trạng thái nghỉ / đếm lượt
        private static bool IsResting(string name, out int secondsLeft)
        {
            lock (StateGate)
            {
                Rest rest;
                secondsLeft = 0;
                if (!Resting.TryGetValue(name, out rest)) return false;
                double left = (rest.Until - DateTime.UtcNow).TotalSeconds;
                if (left <= 0) { Resting.Remove(name); return false; }
                secondsLeft = (int)Math.Ceiling(left);
                return true;
            }
        }

        private static void PutToRest(string name, int seconds, string why)
        {
            lock (StateGate)
            {
                Rest rest = new Rest();
                rest.Until = DateTime.UtcNow.AddSeconds(seconds);
                rest.Why = why;
                Resting[name] = rest;
            }
        }

        // Người dùng vừa đổi khóa/thứ tự trong Cài đặt: cho mọi nhà cung cấp dùng lại ngay.
        internal static void ResetRest()
        {
            lock (StateGate) { Resting.Clear(); }
        }

        private static int Count(string name, bool add)
        {
            string today = DateTime.Now.ToString("yyyyMMdd");
            lock (StateGate)
            {
                Counter c;
                if (!Counters.TryGetValue(name, out c)) { c = new Counter(); Counters[name] = c; }
                if (c.Day != today) { c.Day = today; c.Count = 0; }
                if (add) c.Count++;
                return c.Count;
            }
        }

        // Trạng thái từng nhà cung cấp cho điện thoại hiển thị (status.get).
        internal static List<object> ProviderStatus()
        {
            List<object> list = new List<object>();
            foreach (string name in Settings.NormalizeOrder(Settings.Current.ProviderOrder).Split(','))
            {
                IProvider p = ByName(name);
                if (p == null) continue;
                int left;
                bool resting = IsResting(name, out left);
                string why = "";
                lock (StateGate) { Rest r; if (resting && Resting.TryGetValue(name, out r)) why = r.Why; }
                list.Add(Json.D("name", name, "label", p.Label, "configured", p.Configured, "off", Settings.Current.IsProviderOff(name), "rest_sec", resting ? left : 0, "why", why, "today", Count(name, false)));
            }
            return list;
        }

        // Thứ tự thử: theo ProviderOrder, chỉ nhà đã cấu hình và đang không nghỉ.
        // Nếu tất cả đang nghỉ thì thử nhà sắp hết nghỉ sớm nhất (còn hơn báo lỗi suông).
        internal static List<IProvider> Order()
        {
            List<IProvider> ready = new List<IProvider>();
            IProvider soonest = null;
            int soonestLeft = int.MaxValue;
            foreach (string name in Settings.NormalizeOrder(Settings.Current.ProviderOrder).Split(','))
            {
                IProvider p = ByName(name);
                if (p == null || !p.Configured || Settings.Current.IsProviderOff(name)) continue;
                int left;
                if (!IsResting(name, out left)) ready.Add(p);
                else if (left < soonestLeft) { soonest = p; soonestLeft = left; }
            }
            if (ready.Count == 0 && soonest != null) ready.Add(soonest);
            if (!Settings.Current.Failover && ready.Count > 1) ready.RemoveRange(1, ready.Count - 1);
            return ready;
        }

        internal string Transcribe(byte[] wav, CommandContext cmd)
        {
            return Gemini.Transcribe(wav, "audio/wav", cmd);
        }

        internal void Reset(string conversation)
        {
            lock (history) history.Remove(conversation ?? "");
        }

        internal AgentResult Chat(string conversation, string text, ToolContext ctx, CommandContext cmd)
        {
            AgentResult result = new AgentResult();
            string cmdId = cmd == null ? null : cmd.Id;
            List<IProvider> providers = Order();
            if (providers.Count == 0)
            {
                result.Error = "Mình chưa có khóa API nào. Mở Cài đặt trên điện thoại và nhập khóa Gemini, Groq, OpenRouter hoặc NVIDIA " +
                               "(hoặc bật Cloudflare Worker, không cần khóa).";
                return result;
            }

            List<ChatMsg> past;
            lock (history)
            {
                List<ChatMsg> stored;
                past = history.TryGetValue(conversation ?? "", out stored) ? new List<ChatMsg>(stored) : new List<ChatMsg>();
            }
            ChatMsg user = new ChatMsg();
            user.Role = "user";
            user.Text = text;
            string system = Persona.Build(Settings.Current, ScreenTools.Info());

            List<string> failures = new List<string>();
            string firstFailed = null;
            string usedProvider = null;
            for (int i = 0; i < providers.Count; i++)
            {
                IProvider provider = providers[i];
                if (cmd != null) cmd.ThrowIfCancelled();
                AgentResult attempt = new AgentResult();
                try
                {
                    Loop(provider, system, past, user, ctx, cmd, attempt);
                    result = attempt;
                    usedProvider = provider.Name;
                    break;
                }
                catch (AiException error)
                {
                    string next = i + 1 < providers.Count ? providers[i + 1].Label : null;
                    if (error.Soft)
                    {
                        failures.Add(provider.Label + ": " + error.Message);
                        Log.Info("AI", provider.Label + " bỏ qua lệnh này (" + error.Message + ")" + (next != null ? " -> thử " + next : ""), cmdId);
                    }
                    else
                    {
                        int rest = RestFor(error, provider);
                        PutToRest(provider.Name, rest, error.Message);
                        failures.Add(provider.Label + ": " + error.Message);
                        if (firstFailed == null) firstFailed = provider.Label;
                        Log.Add("WARN", "AI", provider.Label + " lỗi: " + error.Message + (next != null ? " -> thử " + next : "") +
                            " (nghỉ " + rest + "s)", error.Body, cmdId);
                    }
                    if (ctx != null && ctx.ToolsRan)
                    {
                        // Đã chạy hành động thật rồi (vd. đã mở tab): KHÔNG làm lại bằng nhà cung cấp khác (tránh làm hai lần)
                        // và KHÔNG báo lỗi suông - luôn trả lời cho người dùng biết việc đã làm xong, dù lượt AI thứ hai bị hết hạn mức.
                        attempt.Text = DoneWithoutAi(attempt, provider.Label, error.Message);
                        result = attempt;
                        usedProvider = provider.Name;
                        break;
                    }
                }
            }

            if (usedProvider == null)
            {
                result.Error = failures.Count == 1 ? failures[0] : "Mọi nhà cung cấp AI đều không dùng được:\n- " + string.Join("\n- ", failures.ToArray());
                return result;
            }

            result.Provider = usedProvider;
            if (firstFailed != null) result.Switched = firstFailed + " lỗi/hết lượt, đã chuyển sang " + ByName(usedProvider).Label;
            if (cmd != null)
            {
                cmd.AiProvider = ByName(usedProvider).Label;
                cmd.AiCountToday = Count(usedProvider, false);
            }

            ChatMsg reply = new ChatMsg();
            reply.Role = "assistant";
            reply.Text = result.Text;
            reply.Provider = usedProvider;
            lock (history)
            {
                if (history.Count > 40) history.Clear();
                List<ChatMsg> list;
                if (!history.TryGetValue(conversation ?? "", out list)) { list = new List<ChatMsg>(); history[conversation ?? ""] = list; }
                list.Add(user);
                list.Add(reply);
                while (list.Count > MaxHistory) list.RemoveAt(0);
                while (list.Count > 0 && list[0].Role != "user") list.RemoveAt(0);
            }
            return result;
        }

        // Câu trả lời dựng từ kết quả công cụ khi AI không diễn giải được nữa.
        private static string DoneWithoutAi(AgentResult attempt, string label, string why)
        {
            StringBuilder b = new StringBuilder();
            bool anyFail = false;
            foreach (ActionNote n in attempt.Notes)
            {
                if (b.Length > 0) b.Append("; ");
                b.Append(n.Ok ? "" : "LỖI: ").Append(Tools.Shorten(n.Text, 90));
                if (!n.Ok) anyFail = true;
            }
            string head = anyFail ? "Mình đã thử làm nhưng có lỗi: " : "Đã làm xong: ";
            return head + b + ". (" + label + " hết lượt/lỗi nên mình không nói thêm được.)";
        }

        // Nhà cung cấp nghỉ bao lâu sau lỗi này.
        private static int RestFor(AiException error, IProvider provider)
        {
            int sec;
            if (error.Status == 429)
            {
                if (error.RetryAfterSec > 0) sec = Math.Min(Math.Max(error.RetryAfterSec, 5), 1800);
                else if (provider.Name == "cloudflare" || Http.LooksDaily(error.Body)) sec = RestDailySec;
                else sec = RestRateLimitSec;
            }
            else if (error.Status == 400 || error.Status == 401 || error.Status == 402 || error.Status == 403 || error.Status == 404) sec = RestBadKeySec;
            else sec = RestTransientSec;
            return sec;
        }

        // Công cụ mà kết quả CẦN AI diễn đạt lại cho người dùng (không bỏ qua lượt AI thứ hai).
        private static bool NeedsAiAnswer(string tool)
        {
            return tool == "list_windows" || tool == "get_active_window" || tool == "look_at_screen";
        }

        private static void Loop(IProvider provider, string system, List<ChatMsg> past, ChatMsg user, ToolContext ctx, CommandContext cmd, AgentResult result)
        {
            string cmdId = cmd == null ? null : cmd.Id;
            List<ChatMsg> work = new List<ChatMsg>(past);
            work.Add(user);
            for (int round = 0; round < MaxRounds; round++)
            {
                if (cmd != null) cmd.ThrowIfCancelled();
                bool local = provider.Name == "cloudflare" && work[work.Count - 1].Role == "tool";   // vòng 2 của Cloudflare không dùng mạng
                Stopwatch watch = Stopwatch.StartNew();
                AiTurn turn = provider.Complete(system, work, Tools.Specs(), cmd);
                watch.Stop();
                if (!local)
                {
                    int n = Count(provider.Name, true);
                    if (cmd != null) { cmd.AddAi(watch.ElapsedMilliseconds); cmd.AiCountToday = n; }
                    Log.Info("AI", provider.Label + " trả lời sau " + CommandContext.Sec(watch.ElapsedMilliseconds) + " (lượt thứ " + n + " hôm nay)", cmdId);
                }

                if (turn.Calls.Count == 0)
                {
                    result.Text = turn.Text;
                    return;
                }
                ChatMsg assistant = new ChatMsg();
                assistant.Role = "assistant";
                assistant.Text = turn.Text;
                assistant.Calls = turn.Calls;
                assistant.Provider = provider.Name;
                assistant.Raw = turn.Raw;
                work.Add(assistant);

                bool allOk = true;
                bool needAnswer = false;
                string lastOutcome = "";
                foreach (ToolCall call in turn.Calls)
                {
                    if (cmd != null) cmd.ThrowIfCancelled();
                    if (ctx != null && ctx.Progress != null) ctx.Progress(Tools.Describe(call.Name, call.Args));
                    Stopwatch toolWatch = Stopwatch.StartNew();
                    ToolOutcome outcome = Tools.RunTimed(call.Name, call.Args, ctx, cmd);
                    toolWatch.Stop();
                    if (cmd != null) cmd.AddTool(toolWatch.ElapsedMilliseconds);
                    if (ctx != null) ctx.ToolsRan = true;
                    Log.Add(outcome.Ok ? "INFO" : "WARN", "Công cụ", call.Name + (outcome.Ok ? " OK" : " THẤT BẠI") + " (" + CommandContext.Sec(toolWatch.ElapsedMilliseconds) + "): " +
                        Tools.Shorten(outcome.Text, 160), null, cmdId);
                    ActionNote note = new ActionNote();
                    note.Tool = call.Name;
                    note.Ok = outcome.Ok;
                    note.Text = outcome.Text;
                    result.Notes.Add(note);
                    if (outcome.Image != null) result.Image = outcome.Image;
                    if (!outcome.Ok) allOk = false;
                    if (NeedsAiAnswer(call.Name)) needAnswer = true;
                    lastOutcome = outcome.Text;

                    ChatMsg tool = new ChatMsg();
                    tool.Role = "tool";
                    tool.CallId = call.Id;
                    tool.CallName = call.Name;
                    tool.Text = (outcome.Ok ? "OK: " : "FAILED: ") + outcome.Text;
                    work.Add(tool);
                }

                // Tiết kiệm lượt AI (gói miễn phí rất ít): mọi công cụ đều OK và không cần diễn giải thì không gọi AI lần hai.
                if (Settings.Current.SaveQuota && allOk && !needAnswer && provider.Name != "cloudflare")
                {
                    result.Text = turn.Text.Length > 0 ? turn.Text : "Xong rồi! " + Tools.Shorten(lastOutcome, 100);
                    return;
                }
            }
            if (result.Text.Length == 0) result.Text = "Xong rồi.";
        }
    }
}
