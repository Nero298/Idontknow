using System.Collections.Generic;
using CapriHost.Core;

namespace CapriHost.Ai
{
    // Groq (api.groq.com/openai/v1), mặc định openai/gpt-oss-20b. Rất nhanh.
    internal sealed class GroqProvider : OpenAiStyleProvider
    {
        public override string Name { get { return "groq"; } }
        public override string Label { get { return "Groq"; } }
        protected override string BaseUrl { get { return "https://api.groq.com/openai/v1"; } }
        protected override string Key { get { return Settings.Current.GroqKey; } }
        protected override string Model { get { return Settings.Current.GroqModel; } }
        protected override string KeyHint { get { return "Khóa phải bắt đầu bằng gsk_. "; } }

        protected override void ExtraBody(Dictionary<string, object> body)
        {
            // gpt-oss là mô hình suy luận: mức "low" cho câu trả lời nhanh, đủ cho điều khiển máy.
            if (Settings.Current.GroqModel.IndexOf("gpt-oss", System.StringComparison.OrdinalIgnoreCase) >= 0)
                body["reasoning_effort"] = "low";
        }

        protected override bool KeepModel(string id)
        {
            string lower = id.ToLowerInvariant();
            return !(lower.Contains("whisper") || lower.Contains("guard") || lower.Contains("tts") || lower.Contains("playai"));
        }
    }
}
