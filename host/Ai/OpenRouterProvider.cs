using System.Collections.Generic;
using CapriHost.Core;

namespace CapriHost.Ai
{
    // OpenRouter (openrouter.ai/api/v1). Mặc định openrouter/auto: tự chọn mô hình; dùng được mô hình ":free".
    internal sealed class OpenRouterProvider : OpenAiStyleProvider
    {
        public override string Name { get { return "openrouter"; } }
        public override string Label { get { return "OpenRouter"; } }
        protected override string BaseUrl { get { return "https://openrouter.ai/api/v1"; } }
        protected override string Key { get { return Settings.Current.OpenRouterKey; } }
        protected override string Model { get { return Settings.Current.OpenRouterModel; } }
        protected override string KeyHint { get { return "Khóa phải bắt đầu bằng sk-or-. "; } }

        protected override void ExtraHeaders(Dictionary<string, string> headers)
        {
            headers["X-OpenRouter-Title"] = "Capri Remote";
        }

        protected override bool KeepModel(string id)
        {
            string lower = id.ToLowerInvariant();
            return !(lower.Contains("embed") || lower.Contains("whisper") || lower.Contains("tts") || lower.Contains("image") || lower.Contains("moderation"));
        }
    }
}
