using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CapriHost.Core;

namespace CapriHost.Ai
{
    // Lớp nền cho mọi nhà cung cấp theo chuẩn OpenAI /chat/completions (NVIDIA, OpenRouter, Groq).
    // Lớp con chỉ khai báo địa chỉ, khóa, mô hình và vài chi tiết riêng.
    internal abstract class OpenAiStyleProvider : IProvider
    {
        public abstract string Name { get; }
        public abstract string Label { get; }
        protected abstract string BaseUrl { get; }
        protected abstract string Key { get; }
        protected abstract string Model { get; }
        protected virtual int MaxTokens { get { return 1024; } }
        protected virtual string KeyHint { get { return ""; } }
        // > 0: số ký tự đầu vào tối đa (system + công cụ + lịch sử). Vượt thì bỏ bớt tin cũ nhất. 0 = không giới hạn.
        protected virtual int InputBudgetChars { get { return 0; } }

        public bool Configured { get { return Key.Length > 0; } }

        // Header riêng của từng nhà cung cấp (ví dụ OpenRouter).
        protected virtual void ExtraHeaders(Dictionary<string, string> headers) { }
        // Trường riêng trong thân yêu cầu (ví dụ reasoning_effort của Groq).
        protected virtual void ExtraBody(Dictionary<string, object> body) { }
        // Lọc danh sách mô hình.
        protected virtual bool KeepModel(string id) { return true; }

        private Dictionary<string, string> Headers(string key)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers["Authorization"] = "Bearer " + key;
            ExtraHeaders(headers);
            return headers;
        }

        protected AiException Friendly(AiException error)
        {
            if (error.Status == 0) return error;
            string detail = Http.ErrorMessage(error.Body);
            string message;
            if (error.Status == 401 || error.Status == 403) message = Label + " từ chối khóa API (" + error.Status + "). " + KeyHint + "Kiểm tra khóa trong Cài đặt.";
            else if (error.Status == 404) message = "Không tìm thấy mô hình " + Label + " '" + Model + "'. Bấm 'Tải danh sách mô hình' trong Cài đặt để chọn lại.";
            else if (error.Status == 402) message = Label + " báo hết tín dụng (402). " + detail;
            else if (error.Status == 429) message = Label + " đang giới hạn tốc độ hoặc hết hạn mức. " + detail;
            else if (error.Status >= 500) message = "Máy chủ " + Label + " đang lỗi (" + error.Status + "). Thử lại sau.";
            else message = Label + " báo lỗi " + error.Status + ": " + detail;
            AiException result = new AiException(message, error.Status, error.Body);
            result.RetryAfterSec = error.RetryAfterSec;
            return result;
        }

        public AiTurn Complete(string system, List<ChatMsg> messages, List<ToolSpec> tools, CommandContext cmd)
        {
            bool useTools = tools != null && tools.Count > 0;
            try { return Send(system, messages, tools, useTools, cmd); }
            catch (AiException error)
            {
                // Một số mô hình không hỗ trợ gọi công cụ: thử lại không có công cụ (chỉ trò chuyện).
                if (useTools && (error.Status == 400 || error.Status == 404 || error.Status == 422) &&
                    error.Body.IndexOf("tool", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Log.Warn("AI", Label + " không hỗ trợ gọi công cụ với mô hình này: chỉ trò chuyện", cmd == null ? null : cmd.Id);
                    try { return Send(system + "\nTools are not available in this session; just answer in words.", messages, tools, false, cmd); }
                    catch (AiException retry) { throw Friendly(retry); }
                }
                throw Friendly(error);
            }
        }

        private AiTurn Send(string system, List<ChatMsg> messages, List<ToolSpec> tools, bool useTools, CommandContext cmd)
        {
            int budget = InputBudgetChars;
            if (budget > 0) messages = Trim(system, messages, useTools ? tools : null, budget);
            List<object> wire = new List<object>();
            wire.Add(Json.D("role", "system", "content", system));
            foreach (ChatMsg m in messages)
            {
                if (m.Role == "user") wire.Add(Json.D("role", "user", "content", m.Text));
                else if (m.Role == "tool")
                {
                    if (useTools) wire.Add(Json.D("role", "tool", "tool_call_id", m.CallId, "content", m.Text));
                    else wire.Add(Json.D("role", "user", "content", "[result of " + m.CallName + "] " + m.Text));
                }
                else if (m.Role == "assistant")
                {
                    if (m.Calls.Count > 0 && useTools)
                    {
                        List<object> calls = new List<object>();
                        foreach (ToolCall call in m.Calls)
                            calls.Add(Json.D("id", call.Id, "type", "function", "function", Json.D("name", call.Name, "arguments", Json.Write(call.Args))));
                        wire.Add(Json.D("role", "assistant", "content", m.Text ?? "", "tool_calls", calls));
                    }
                    else wire.Add(Json.D("role", "assistant", "content", string.IsNullOrEmpty(m.Text) ? "..." : m.Text));
                }
            }

            Dictionary<string, object> body = Json.D("model", Model, "messages", wire,
                "max_tokens", MaxTokens, "temperature", 0.8, "stream", false);
            if (useTools)
            {
                List<object> specs = new List<object>();
                foreach (ToolSpec tool in tools)
                    specs.Add(Json.D("type", "function", "function", Json.D("name", tool.Name, "description", tool.Description, "parameters", tool.Schema())));
                body["tools"] = specs;
            }
            ExtraBody(body);

            string text = Http.Text("POST", BaseUrl + "/chat/completions", Json.Write(body), Headers(Key), Timeouts.HttpMs, cmd);
            Dictionary<string, object> root = Json.ReadObject(text);
            List<object> choices = Json.Arr(root, "choices");
            if (choices.Count == 0)
            {
                // OpenRouter/Groq đôi khi trả 200 kèm {"error":{...}}
                Dictionary<string, object> err = Json.Obj(root, "error");
                if (err != null) throw new AiException(Label + " báo lỗi: " + Json.Str(err, "message", "?"), (int)Json.Num(err, "code", 200), text);
                throw new AiException(Label + " không trả lời.", 200, text);
            }
            Dictionary<string, object> message = Json.Obj(choices[0] as Dictionary<string, object>, "message");

            AiTurn turn = new AiTurn();
            string content = Json.Str(message, "content", "");
            // Mô hình suy luận có thể chèn khối <think>...</think>: bỏ đi.
            content = Regex.Replace(content, "<think>.*?</think>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
            turn.Text = content;
            int index = 0;
            foreach (object item in Json.Arr(message, "tool_calls"))
            {
                Dictionary<string, object> call = item as Dictionary<string, object>;
                Dictionary<string, object> function = Json.Obj(call, "function");
                if (function == null) continue;
                ToolCall toolCall = new ToolCall();
                toolCall.Id = Json.Str(call, "id", "call_" + index);
                index++;
                toolCall.Name = Json.Str(function, "name", "");
                object arguments;
                if (function.TryGetValue("arguments", out arguments) && arguments != null)
                {
                    Dictionary<string, object> parsed = arguments as Dictionary<string, object>;
                    if (parsed == null)
                    {
                        try { parsed = Json.ReadObject(Convert.ToString(arguments)); }
                        catch (ArgumentException) { parsed = null; }
                        catch (InvalidOperationException) { parsed = null; }
                    }
                    if (parsed != null) toolCall.Args = parsed;
                }
                turn.Calls.Add(toolCall);
            }
            if (turn.Calls.Count == 0 && turn.Text.Length == 0) throw new AiException(Label + " trả về nội dung rỗng.", 200, text);
            return turn;
        }

        // Cắt lịch sử cho vừa hạn mức đầu vào: bỏ tin cũ nhất, nhưng luôn giữ tin cuối và không để bắt đầu bằng tin "tool".
        private static List<ChatMsg> Trim(string system, List<ChatMsg> messages, List<ToolSpec> tools, int budget)
        {
            int fixedChars = system.Length;
            if (tools != null) foreach (ToolSpec t in tools) fixedChars += t.Name.Length + t.Description.Length + Json.Write(t.Schema()).Length;
            List<ChatMsg> kept = new List<ChatMsg>(messages);
            while (kept.Count > 1 && fixedChars + Size(kept) > budget) kept.RemoveAt(0);
            while (kept.Count > 1 && kept[0].Role != "user") kept.RemoveAt(0);
            return kept;
        }

        private static int Size(List<ChatMsg> list)
        {
            int total = 0;
            foreach (ChatMsg m in list)
            {
                total += (m.Text ?? "").Length + 20;
                foreach (ToolCall c in m.Calls) total += c.Name.Length + Json.Write(c.Args).Length;
            }
            return total;
        }

        public List<string> ListModels(string keyOverride)
        {
            string key = string.IsNullOrEmpty(keyOverride) ? Key : Settings.CleanKey(keyOverride);
            if (key.Length == 0) throw new AiException("Chưa có khóa " + Label + ".", 0, "");
            string text;
            try { text = Http.Text("GET", BaseUrl + "/models", null, Headers(key), Timeouts.HttpListModelsMs); }
            catch (AiException error) { throw Friendly(error); }
            Dictionary<string, object> root = Json.ReadObject(text);
            List<string> found = new List<string>();
            foreach (object item in Json.Arr(root, "data"))
            {
                string id = Json.Str(item as Dictionary<string, object>, "id", "");
                if (id.Length == 0 || !KeepModel(id)) continue;
                found.Add(id);
            }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }
    }
}
