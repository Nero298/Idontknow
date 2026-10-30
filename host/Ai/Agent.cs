using System;
using System.Collections.Generic;
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
        internal byte[] Image;                       // ảnh chụp màn hình (nếu có)
        internal readonly List<ActionNote> Notes = new List<ActionNote>();
    }

    // "Bộ não": nhận câu nói, hỏi LLM (Gemini chính, NVIDIA dự phòng hoặc ngược lại), chạy công cụ AI yêu cầu,
    // lặp đến khi có câu trả lời cuối. Lịch sử hội thoại giữ trong RAM, tối đa 24 tin mỗi cuộc trò chuyện.
    internal sealed class Agent
    {
        internal static readonly GeminiProvider Gemini = new GeminiProvider();
        internal static readonly NvidiaProvider Nvidia = new NvidiaProvider();
        private const int MaxHistory = 24;
        private const int MaxRounds = 6;

        private readonly Dictionary<string, List<ChatMsg>> history = new Dictionary<string, List<ChatMsg>>();

        // Thứ tự thử: nhà cung cấp chính, rồi nhà còn lại nếu bật dự phòng (hoặc nhà chính chưa có khóa).
        internal static List<IProvider> Order()
        {
            Settings s = Settings.Current;
            IProvider first = s.Primary == "nvidia" ? (IProvider)Nvidia : Gemini;
            IProvider second = s.Primary == "nvidia" ? (IProvider)Gemini : Nvidia;
            List<IProvider> list = new List<IProvider>();
            if (first.Configured) list.Add(first);
            if (second.Configured && (s.Failover || !first.Configured)) list.Add(second);
            return list;
        }

        internal string Transcribe(byte[] wav)
        {
            return Gemini.Transcribe(wav, "audio/wav");
        }

        internal void Reset(string conversation)
        {
            lock (history) history.Remove(conversation ?? "");
        }

        internal AgentResult Chat(string conversation, string text, ToolContext ctx)
        {
            AgentResult result = new AgentResult();
            List<IProvider> providers = Order();
            if (providers.Count == 0)
            {
                result.Error = "Mình chưa có khóa API nào. Mở Cài đặt trên điện thoại và nhập khóa Gemini hoặc NVIDIA nhé.";
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

            AiException last = null;
            string usedProvider = null;
            foreach (IProvider provider in providers)
            {
                try
                {
                    Loop(provider, system, past, user, ctx, result);
                    usedProvider = provider.Name;
                    last = null;
                    break;
                }
                catch (AiException error)
                {
                    last = error;
                    Log.Error(provider.Label + " lỗi", error);
                    if (ctx.ToolsRan) break;        // đã chạy hành động thật thì không làm lại bằng nhà cung cấp khác
                }
            }

            if (last != null)
            {
                result.Error = last.Message;
                return result;
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

        private static void Loop(IProvider provider, string system, List<ChatMsg> past, ChatMsg user, ToolContext ctx, AgentResult result)
        {
            List<ChatMsg> work = new List<ChatMsg>(past);
            work.Add(user);
            for (int round = 0; round < MaxRounds; round++)
            {
                AiTurn turn = provider.Complete(system, work, Tools.Specs());
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

                foreach (ToolCall call in turn.Calls)
                {
                    if (ctx.Progress != null) ctx.Progress(Tools.Describe(call.Name, call.Args));
                    ToolOutcome outcome = Tools.Run(call.Name, call.Args, ctx);
                    ctx.ToolsRan = true;
                    ActionNote note = new ActionNote();
                    note.Tool = call.Name;
                    note.Ok = outcome.Ok;
                    note.Text = outcome.Text;
                    result.Notes.Add(note);
                    if (outcome.Image != null) result.Image = outcome.Image;

                    ChatMsg tool = new ChatMsg();
                    tool.Role = "tool";
                    tool.CallId = call.Id;
                    tool.CallName = call.Name;
                    tool.Text = (outcome.Ok ? "OK: " : "FAILED: ") + outcome.Text;
                    work.Add(tool);
                }
            }
            if (result.Text.Length == 0) result.Text = "Xong rồi.";
        }
    }
}
