using System;
using System.Collections.Generic;
using CapriHost.Core;

namespace CapriHost.Ai
{
    // LiteRouter (api.literouter.com/v1) - DeepSeek V4.1 Flash. Chuẩn OpenAI.
    // Biến thể ":free" bị giới hạn cứng 5.000 token đầu vào mỗi yêu cầu, cách nhau ~6-8 giây:
    // nên Capri tự cắt bớt lịch sử cũ khi gọi nhà này (xem InputBudgetChars).
    internal sealed class LiteRouterProvider : OpenAiStyleProvider
    {
        public override string Name { get { return "literouter"; } }
        public override string Label { get { return "LiteRouter"; } }
        protected override string BaseUrl { get { return "https://api.literouter.com/v1"; } }
        protected override string Key { get { return Settings.Current.LiteRouterKey; } }
        protected override string Model { get { return Settings.Current.LiteRouterModel; } }
        protected override string KeyHint { get { return "Lấy khóa tại literouter.com (mục API keys). "; } }

        protected override int InputBudgetChars
        {
            get { return Settings.Current.LiteRouterModel.EndsWith(":free", StringComparison.OrdinalIgnoreCase) ? 9000 : 0; }
        }

        protected override bool KeepModel(string id)
        {
            string lower = id.ToLowerInvariant();
            return !(lower.Contains("embed") || lower.Contains("whisper") || lower.Contains("tts") || lower.Contains("image") || lower.Contains("moderation"));
        }
    }
}
