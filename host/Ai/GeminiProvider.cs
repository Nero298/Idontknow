using System;
using System.Collections.Generic;
using System.Text;
using CapriHost.Core;

namespace CapriHost.Ai
{
    // Google Gemini (generativelanguage.googleapis.com). LLM chính của Capri Host.
    // Ngoài chat + gọi công cụ, Gemini còn đảm nhận nghe giọng nói (STT) và nhìn màn hình.
    internal sealed class GeminiProvider : IProvider
    {
        private const string Base = "https://generativelanguage.googleapis.com/v1beta";

        public string Name { get { return "gemini"; } }
        public string Label { get { return "Gemini"; } }
        public bool Configured { get { return Settings.Current.GeminiKey.Length > 0; } }

        private static Dictionary<string, string> Headers(string key)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers["x-goog-api-key"] = key;
            return headers;
        }

        private static AiException Friendly(AiException error)
        {
            if (error.Status == 0) return error;
            string detail = Http.ErrorMessage(error.Body);
            string message;
            if (error.Status == 400 && detail.IndexOf("API key", StringComparison.OrdinalIgnoreCase) >= 0) message = "Khóa Gemini không hợp lệ. Kiểm tra lại trong Cài đặt.";
            else if (error.Status == 401 || error.Status == 403) message = "Gemini từ chối khóa API (" + error.Status + "). Kiểm tra khóa hoặc quyền truy cập.";
            else if (error.Status == 404) message = "Không tìm thấy mô hình Gemini '" + Settings.Current.GeminiModel + "'. Chọn mô hình khác trong Cài đặt.";
            else if (error.Status == 429) message = "Gemini hết hạn mức (gói miễn phí chỉ có khoảng 20 lượt mỗi ngày) hoặc đang giới hạn tốc độ. " + detail;
            else if (error.Status >= 500) message = "Máy chủ Gemini đang lỗi (" + error.Status + "). Thử lại sau.";
            else message = "Gemini báo lỗi " + error.Status + ": " + detail;
            AiException result = new AiException(message, error.Status, error.Body);
            result.RetryAfterSec = error.RetryAfterSec;
            return result;
        }

        private string Url(string model, string method)
        {
            return Base + "/models/" + Uri.EscapeDataString(model) + ":" + method;
        }

        public AiTurn Complete(string system, List<ChatMsg> messages, List<ToolSpec> tools, CommandContext cmd)
        {
            List<object> contents = new List<object>();
            List<object> pendingResponses = new List<object>();
            foreach (ChatMsg m in messages)
            {
                if (m.Role == "tool")
                {
                    pendingResponses.Add(Json.D("functionResponse", Json.D("name", m.CallName, "response", Json.D("result", m.Text))));
                    continue;
                }
                if (pendingResponses.Count > 0)
                {
                    contents.Add(Json.D("role", "user", "parts", pendingResponses));
                    pendingResponses = new List<object>();
                }
                if (m.Role == "user")
                {
                    contents.Add(Json.D("role", "user", "parts", new object[] { Json.D("text", m.Text) }));
                }
                else if (m.Role == "assistant")
                {
                    Dictionary<string, object> raw = m.Raw as Dictionary<string, object>;
                    if (raw != null && m.Provider == "gemini")
                    {
                        contents.Add(raw);      // giữ nguyên, kể cả thoughtSignature
                    }
                    else
                    {
                        List<object> parts = new List<object>();
                        if (!string.IsNullOrEmpty(m.Text)) parts.Add(Json.D("text", m.Text));
                        foreach (ToolCall call in m.Calls) parts.Add(Json.D("functionCall", Json.D("name", call.Name, "args", call.Args)));
                        if (parts.Count == 0) parts.Add(Json.D("text", "..."));
                        contents.Add(Json.D("role", "model", "parts", parts));
                    }
                }
            }
            if (pendingResponses.Count > 0) contents.Add(Json.D("role", "user", "parts", pendingResponses));

            Dictionary<string, object> body = Json.D(
                "systemInstruction", Json.D("parts", new object[] { Json.D("text", system) }),
                "contents", contents,
                "generationConfig", Json.D("temperature", 0.9, "maxOutputTokens", 2048));
            if (tools != null && tools.Count > 0)
            {
                List<object> declarations = new List<object>();
                foreach (ToolSpec tool in tools)
                {
                    Dictionary<string, object> declaration = Json.D("name", tool.Name, "description", tool.Description);
                    if (tool.Params.Count > 0) declaration["parameters"] = tool.Schema();   // Gemini từ chối schema rỗng
                    declarations.Add(declaration);
                }
                body["tools"] = new object[] { Json.D("functionDeclarations", declarations) };
            }

            string text;
            try { text = Http.Text("POST", Url(Settings.Current.GeminiModel, "generateContent"), Json.Write(body), Headers(Settings.Current.GeminiKey), Timeouts.HttpMs, cmd); }
            catch (AiException error) { throw Friendly(error); }
            return Parse(text);
        }

        private static AiTurn Parse(string text)
        {
            Dictionary<string, object> root = Json.ReadObject(text);
            List<object> candidates = Json.Arr(root, "candidates");
            AiTurn turn = new AiTurn();
            if (candidates.Count == 0)
            {
                Dictionary<string, object> feedback = Json.Obj(root, "promptFeedback");
                string reason = feedback == null ? "" : Json.Str(feedback, "blockReason", "");
                throw new AiException("Gemini không trả lời" + (reason.Length > 0 ? " (bị chặn: " + reason + ")" : "") + ".", 200, text);
            }
            Dictionary<string, object> candidate = candidates[0] as Dictionary<string, object>;
            Dictionary<string, object> content = Json.Obj(candidate, "content");
            StringBuilder said = new StringBuilder();
            int index = 0;
            foreach (object item in Json.Arr(content, "parts"))
            {
                Dictionary<string, object> part = item as Dictionary<string, object>;
                if (part == null) continue;
                Dictionary<string, object> call = Json.Obj(part, "functionCall");
                if (call != null)
                {
                    ToolCall toolCall = new ToolCall();
                    toolCall.Id = "call_" + (index++);
                    toolCall.Name = Json.Str(call, "name", "");
                    Dictionary<string, object> args = Json.Obj(call, "args");
                    if (args != null) toolCall.Args = args;
                    turn.Calls.Add(toolCall);
                }
                else if (Json.Has(part, "text") && !Json.Bool(part, "thought", false))
                {
                    said.Append(Json.Str(part, "text", ""));
                }
            }
            turn.Text = said.ToString().Trim();
            turn.Raw = content;
            if (turn.Calls.Count == 0 && turn.Text.Length == 0)
            {
                string finish = Json.Str(candidate, "finishReason", "?");
                throw new AiException("Gemini trả về nội dung rỗng (finishReason=" + finish + ").", 200, text);
            }
            return turn;
        }

        // Nghe giọng nói: gửi WAV cho Gemini để lấy văn bản. Hoạt động với mọi ngôn ngữ.
        internal string Transcribe(byte[] wav, string mime, CommandContext cmd)
        {
            if (!Configured) throw new AiException("Cần khóa Gemini để nhận lệnh bằng giọng nói. Nhập khóa trong Cài đặt.", 0, "");
            Dictionary<string, object> body = Json.D(
                "contents", new object[] {
                    Json.D("role", "user", "parts", new object[] {
                        Json.D("inlineData", Json.D("mimeType", mime, "data", Convert.ToBase64String(wav))),
                        Json.D("text", "Transcribe the speech in this audio exactly, in the language spoken. Output only the transcript, nothing else. If there is no intelligible speech, output exactly: [silence]")
                    })
                },
                "generationConfig", Json.D("temperature", 0.0, "maxOutputTokens", 1024));
            string text;
            try { text = Http.Text("POST", Url(Settings.Current.GeminiModel, "generateContent"), Json.Write(body), Headers(Settings.Current.GeminiKey), Timeouts.HttpMediaMs, cmd); }
            catch (AiException error) { throw Friendly(error); }
            return Parse(text).Text.Trim();
        }

        // Nhìn ảnh chụp màn hình và trả lời câu hỏi.
        internal string Describe(byte[] jpeg, string question, CommandContext cmd)
        {
            if (!Configured) throw new AiException("Cần khóa Gemini để nhìn màn hình.", 0, "");
            Dictionary<string, object> body = Json.D(
                "contents", new object[] {
                    Json.D("role", "user", "parts", new object[] {
                        Json.D("inlineData", Json.D("mimeType", "image/jpeg", "data", Convert.ToBase64String(jpeg))),
                        Json.D("text", question)
                    })
                },
                "generationConfig", Json.D("temperature", 0.3, "maxOutputTokens", 700));
            string text;
            try { text = Http.Text("POST", Url(Settings.Current.GeminiModel, "generateContent"), Json.Write(body), Headers(Settings.Current.GeminiKey), Timeouts.HttpMediaMs, cmd); }
            catch (AiException error) { throw Friendly(error); }
            return Parse(text).Text.Trim();
        }

        // Danh sách mô hình hiện có (tên mô hình đổi thường xuyên nên luôn lấy từ API).
        public List<string> ListModels(string keyOverride)
        {
            string key = string.IsNullOrEmpty(keyOverride) ? Settings.Current.GeminiKey : Settings.CleanKey(keyOverride);
            if (key.Length == 0) throw new AiException("Chưa có khóa Gemini.", 0, "");
            List<string> found = new List<string>();
            string token = "";
            for (int page = 0; page < 4; page++)
            {
                string url = Base + "/models?pageSize=200" + (token.Length > 0 ? "&pageToken=" + Uri.EscapeDataString(token) : "");
                string text;
                try { text = Http.Text("GET", url, null, Headers(key), Timeouts.HttpListModelsMs); }
                catch (AiException error) { throw Friendly(error); }
                Dictionary<string, object> root = Json.ReadObject(text);
                foreach (object item in Json.Arr(root, "models"))
                {
                    Dictionary<string, object> model = item as Dictionary<string, object>;
                    string name = Json.Str(model, "name", "");
                    if (!name.StartsWith("models/gemini", StringComparison.Ordinal)) continue;
                    bool chats = false;
                    foreach (object method in Json.Arr(model, "supportedGenerationMethods")) if (Convert.ToString(method) == "generateContent") chats = true;
                    string lower = name.ToLowerInvariant();
                    if (!chats || lower.Contains("embedding") || lower.Contains("-tts") || lower.Contains("image") || lower.Contains("live") || lower.Contains("audio")) continue;
                    found.Add(name.Substring("models/".Length));
                }
                token = Json.Str(root, "nextPageToken", "");
                if (token.Length == 0) break;
            }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            found.Reverse();
            return found;
        }
    }
}
