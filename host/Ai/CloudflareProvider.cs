using System;
using System.Collections.Generic;
using CapriHost.Core;

namespace CapriHost.Ai
{
    // Cloudflare Worker "AI Agent Controller": POST {"command": "..."} -> {"action": {action,target,params,reply}}.
    // KHÔNG cần khóa API (~1.000 lệnh/ngày). Chỉ hiểu lệnh điều khiển máy (không trò chuyện được), nên:
    //  - lệnh nó hiểu  -> đổi thành công cụ của Capri và chạy như AI thường;
    //  - lệnh không hiểu / chưa hỗ trợ -> ném AiException.Soft để Capri thử nhà cung cấp kế tiếp (không bị coi là hỏng).
    // Mặc định xếp SAU Gemini: chỉ dùng khi Gemini hết hạn mức.
    internal sealed class CloudflareProvider : IProvider
    {
        public string Name { get { return "cloudflare"; } }
        public string Label { get { return "Cloudflare Worker"; } }
        public bool Configured { get { return Settings.Current.CloudflareOn && Settings.Current.CloudflareUrl.Length > 0; } }

        public AiTurn Complete(string system, List<ChatMsg> messages, List<ToolSpec> tools, CommandContext cmd)
        {
            ChatMsg last = messages[messages.Count - 1];
            AiTurn turn = new AiTurn();

            if (last.Role == "tool")
            {
                // Vòng 2: công cụ đã chạy xong. Worker đã soạn sẵn câu trả lời; không gọi mạng lần nữa.
                string reply = "Xong rồi.";
                bool allOk = true;
                string failure = "";
                string planned = null;
                for (int i = messages.Count - 1; i >= 0; i--)
                {
                    ChatMsg m = messages[i];
                    if (m.Role == "tool")
                    {
                        if (!m.Text.StartsWith("OK", StringComparison.Ordinal)) { allOk = false; failure = m.Text; }
                    }
                    else
                    {
                        if (m.Role == "assistant") planned = m.Raw as string;
                        break;
                    }
                }
                if (allOk) { if (!string.IsNullOrEmpty(planned)) reply = planned; }
                else reply = "Mình chưa làm được: " + failure.Replace("FAILED: ", "");
                turn.Text = reply;
                return turn;
            }

            string command = last.Text ?? "";
            string body = Json.Write(Json.D("command", command));
            string text;
            try { text = Http.Text("POST", Settings.Current.CloudflareUrl, body, null, Timeouts.HttpMs, cmd); }
            catch (AiException error) { throw Friendly(error); }

            Dictionary<string, object> root;
            try { root = Json.ReadObject(text); }
            catch (ArgumentException) { throw Soft("Worker trả về dữ liệu không phải JSON."); }
            catch (InvalidOperationException) { throw Soft("Worker trả về dữ liệu không phải JSON."); }

            Dictionary<string, object> action = Json.Obj(root, "action");
            if (action == null)
            {
                string why = Json.Str(root, "error", "không có hành động");
                throw Soft("Worker: " + why);
            }
            string kind = Json.Str(action, "action", "unknown");
            string target = (Json.Str(action, "target", "") ?? "").Trim();
            Dictionary<string, object> parameters = Json.Obj(action, "params") ?? new Dictionary<string, object>();
            double confidence = Json.Num(action, "confidence", 1.0);
            string spoken = Json.Str(action, "reply", "Xong rồi.");

            if (kind == "unknown" || confidence < 0.5) throw Soft("Worker không hiểu lệnh này.");

            ToolCall call = Map(kind, target, parameters, command);
            if (call == null) throw Soft("Worker trả hành động '" + kind + "' mà Capri không chạy trực tiếp.");

            call.Id = "cf_0";
            turn.Calls.Add(call);
            turn.Raw = spoken;
            return turn;
        }

        private static AiException Soft(string message)
        {
            AiException error = new AiException(message, 0, "");
            error.Soft = true;
            return error;
        }

        private static AiException Friendly(AiException error)
        {
            if (error.Status == 0) return error;
            string detail = Http.ErrorMessage(error.Body);
            string message;
            int status = error.Status;
            if (status >= 500)
            {
                // Tài liệu Worker: HTTP 500 thường là hết 10.000 neurons/ngày (reset 07:00 sáng giờ Việt Nam).
                message = "Cloudflare Worker hết hạn mức hôm nay hoặc đang lỗi (" + status + "). " + detail;
                status = 429;
            }
            else message = "Cloudflare Worker báo lỗi " + error.Status + ": " + detail;
            AiException result = new AiException(message, status, error.Body);
            result.RetryAfterSec = error.RetryAfterSec;
            return result;
        }

        // Đổi hành động của Worker thành công cụ của Capri. Trả null nếu không có tương ứng (vd. shutdown: cố ý bỏ qua).
        private static ToolCall Map(string kind, string target, Dictionary<string, object> p, string command)
        {
            ToolCall call = new ToolCall();
            string query = Json.Str(p, "query", "");
            switch (kind)
            {
                case "open_app":
                    if (target.Length == 0) return null;
                    call.Name = "open_app"; call.Args["name"] = target; return call;
                case "open_url":
                    {
                        string url = Json.Str(p, "url", "");
                        if (url.Length == 0) url = target;
                        if (url.Length == 0) return null;
                        call.Name = "open_website"; call.Args["url"] = url; return call;
                    }
                case "search":
                    if (query.Length == 0) return null;
                    call.Name = "open_website"; call.Args["url"] = query; return call;
                case "close_app":
                    if (target.Length == 0) return null;
                    call.Name = "close_app"; call.Args["name"] = target; return call;
                case "close_tab":
                    call.Name = "press_keys"; call.Args["keys"] = target == "all" ? "ctrl+shift+w" : "ctrl+w"; return call;
                case "play":
                case "pause":
                    call.Name = "media"; call.Args["action"] = "play_pause"; return call;
                case "next":
                    call.Name = "media"; call.Args["action"] = "next"; return call;
                case "previous":
                    call.Name = "media"; call.Args["action"] = "previous"; return call;
                case "volume_up":
                    call.Name = "volume"; call.Args["action"] = "volume_up"; return call;
                case "volume_down":
                    call.Name = "volume"; call.Args["action"] = "volume_down"; return call;
                case "volume_mute":
                    call.Name = "volume"; call.Args["action"] = "mute"; return call;
                case "fullscreen":
                    call.Name = "window_control"; call.Args["action"] = "fullscreen"; return call;
                case "cleanup":
                    call.Name = "cleanup"; call.Args["what"] = "all"; return call;
                case "type_text":
                    if (query.Length == 0) return null;
                    call.Name = "type_text"; call.Args["text"] = query; return call;
                case "screenshot":
                    call.Name = "screenshot"; return call;
                case "lock_screen":
                    call.Name = "lock_screen"; return call;
            }
            return null;     // shutdown và mọi thứ khác: để AI chính xử lý (có hỏi xác nhận)
        }

        public List<string> ListModels(string keyOverride)
        {
            return new List<string>();
        }
    }
}
