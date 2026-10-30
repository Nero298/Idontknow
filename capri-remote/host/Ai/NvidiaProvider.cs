using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using CapriHost.Core;

namespace CapriHost.Ai
{
    // NVIDIA NIM / AI Endpoints (integrate.api.nvidia.com), giao diện tương thích OpenAI.
    // Dùng làm phương án dự phòng, hoặc làm nhà cung cấp chính nếu người dùng chọn.
    internal sealed class NvidiaProvider : IProvider
    {
        private const string Base = "https://integrate.api.nvidia.com/v1";

        public string Name { get { return "nvidia"; } }
        public string Label { get { return "NVIDIA"; } }
        public bool Configured { get { return Settings.Current.NvidiaKey.Length > 0; } }

        private static Dictionary<string, string> Headers(string key)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers["Authorization"] = "Bearer " + key;
            return headers;
        }

        private static AiException Friendly(AiException error)
        {
            if (error.Status == 0) return error;
            string detail = Http.ErrorMessage(error.Body);
            string message;
            if (error.Status == 401 || error.Status == 403) message = "NVIDIA từ chối khóa API (" + error.Status + "). Khóa phải bắt đầu bằng nvapi-.";
            else if (error.Status == 404) message = "Không tìm thấy mô hình NVIDIA '" + Settings.Current.NvidiaModel + "'. Bấm 'Tải danh sách mô hình' trong Cài đặt để chọn lại.";
            else if (error.Status == 429) message = "NVIDIA đang giới hạn tốc độ. Thử lại sau ít phút.";
            else if (error.Status >= 500) message = "Máy chủ NVIDIA đang lỗi (" + error.Status + "). Thử lại sau.";
            else message = "NVIDIA báo lỗi " + error.Status + ": " + detail;
            return new AiException(message, error.Status, error.Body);
        }

        public AiTurn Complete(string system, List<ChatMsg> messages, List<ToolSpec> tools)
        {
            bool useTools = tools != null && tools.Count > 0;
            try { return Send(system, messages, tools, useTools); }
            catch (AiException error)
            {
                // Một số mô hình NVIDIA không hỗ trợ gọi công cụ: thử lại không có công cụ (chỉ trò chuyện).
                if (useTools && (error.Status == 400 || error.Status == 422) && error.Body.IndexOf("tool", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    try { return Send(system + "\nTools are not available in this session; just answer in words.", messages, tools, false); }
                    catch (AiException retry) { throw Friendly(retry); }
                }
                throw Friendly(error);
            }
        }

        private AiTurn Send(string system, List<ChatMsg> messages, List<ToolSpec> tools, bool useTools)
        {
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

            Dictionary<string, object> body = Json.D("model", Settings.Current.NvidiaModel, "messages", wire,
                "max_tokens", 1024, "temperature", 0.8, "stream", false);
            if (useTools)
            {
                List<object> specs = new List<object>();
                foreach (ToolSpec tool in tools)
                    specs.Add(Json.D("type", "function", "function", Json.D("name", tool.Name, "description", tool.Description, "parameters", tool.Schema())));
                body["tools"] = specs;
            }

            string text = Http.Text("POST", Base + "/chat/completions", Json.Write(body), Headers(Settings.Current.NvidiaKey), 90000);
            Dictionary<string, object> root = Json.ReadObject(text);
            List<object> choices = Json.Arr(root, "choices");
            if (choices.Count == 0) throw new AiException("NVIDIA không trả lời.", 200, text);
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
            if (turn.Calls.Count == 0 && turn.Text.Length == 0) throw new AiException("NVIDIA trả về nội dung rỗng.", 200, text);
            return turn;
        }

        public List<string> ListModels(string keyOverride)
        {
            string key = string.IsNullOrEmpty(keyOverride) ? Settings.Current.NvidiaKey : keyOverride;
            if (key.Length == 0) throw new AiException("Chưa có khóa NVIDIA.", 0, "");
            string text;
            try { text = Http.Text("GET", Base + "/models", null, Headers(key), 25000); }
            catch (AiException error) { throw Friendly(error); }
            Dictionary<string, object> root = Json.ReadObject(text);
            List<string> found = new List<string>();
            Regex skip = new Regex("embed|rerank|reward|guard|safety|parse|clip|retriev|translate|ocr|asr|tts|speech|audio|image|diffusion|segment|depth|pose|classif|detect|video", RegexOptions.IgnoreCase);
            foreach (object item in Json.Arr(root, "data"))
            {
                string id = Json.Str(item as Dictionary<string, object>, "id", "");
                if (id.Length == 0 || skip.IsMatch(id)) continue;
                found.Add(id);
            }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }
    }
}
