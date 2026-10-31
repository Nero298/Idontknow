using System;
using System.Collections.Generic;

namespace CapriHost.Ai
{
    // Lỗi từ nhà cung cấp AI (HTTP hoặc mạng). Message luôn là tiếng Việt dễ hiểu cho người dùng.
    internal sealed class AiException : Exception
    {
        internal readonly int Status;
        internal readonly string Body;
        internal int RetryAfterSec;      // từ header Retry-After (0 nếu không có)
        internal bool Soft;              // true: nhà cung cấp chỉ "không xử lý được lệnh này" (không phải hỏng) -> thử nhà tiếp theo, không nghỉ

        internal AiException(string message, int status, string body) : base(message)
        {
            Status = status;
            Body = body ?? "";
        }
    }

    internal sealed class ToolParam
    {
        internal string Name;
        internal string Type = "string";        // string | integer | number | boolean
        internal string Description = "";
        internal bool Required;
        internal string[] Choices;              // nếu có: giá trị cho phép

        internal ToolParam(string name, string type, string description, bool required, params string[] choices)
        {
            Name = name;
            Type = type;
            Description = description;
            Required = required;
            Choices = choices != null && choices.Length > 0 ? choices : null;
        }
    }

    // Mô tả một công cụ (hành động) mà AI được phép gọi. Dùng chung cho Gemini và NVIDIA.
    internal sealed class ToolSpec
    {
        internal string Name;
        internal string Description;
        internal readonly List<ToolParam> Params = new List<ToolParam>();

        internal ToolSpec(string name, string description, params ToolParam[] parameters)
        {
            Name = name;
            Description = description;
            if (parameters != null) Params.AddRange(parameters);
        }

        // JSON Schema kiểu OpenAPI (chữ thường), cả Gemini lẫn OpenAI đều đọc được.
        internal Dictionary<string, object> Schema()
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            List<object> required = new List<object>();
            foreach (ToolParam p in Params)
            {
                Dictionary<string, object> prop = new Dictionary<string, object>();
                prop["type"] = p.Type;
                prop["description"] = p.Description;
                if (p.Choices != null)
                {
                    List<object> choices = new List<object>();
                    foreach (string c in p.Choices) choices.Add(c);
                    prop["enum"] = choices;
                }
                properties[p.Name] = prop;
                if (p.Required) required.Add(p.Name);
            }
            Dictionary<string, object> schema = new Dictionary<string, object>();
            schema["type"] = "object";
            schema["properties"] = properties;
            if (required.Count > 0) schema["required"] = required;
            return schema;
        }
    }

    internal sealed class ToolCall
    {
        internal string Id;
        internal string Name;
        internal Dictionary<string, object> Args = new Dictionary<string, object>();
    }

    // Một tin trong cuộc trò chuyện. Role: "user" | "assistant" | "tool".
    internal sealed class ChatMsg
    {
        internal string Role;
        internal string Text = "";
        internal List<ToolCall> Calls = new List<ToolCall>();   // chỉ với assistant
        internal string CallId;                                  // chỉ với tool
        internal string CallName;                                // chỉ với tool
        internal string Provider;                                // nhà cung cấp tạo ra tin assistant này
        internal object Raw;                                     // nội dung gốc (Gemini cần giữ nguyên "thoughtSignature")
    }

    // Kết quả một lượt gọi mô hình: hoặc là câu trả lời, hoặc là yêu cầu gọi công cụ.
    internal sealed class AiTurn
    {
        internal string Text = "";
        internal readonly List<ToolCall> Calls = new List<ToolCall>();
        internal object Raw;
    }

    internal interface IProvider
    {
        string Name { get; }
        string Label { get; }
        bool Configured { get; }
        AiTurn Complete(string system, List<ChatMsg> messages, List<ToolSpec> tools, CapriHost.Core.CommandContext cmd);
        List<string> ListModels(string keyOverride);
    }
}
