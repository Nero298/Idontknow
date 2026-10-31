using System;
using System.Text.RegularExpressions;
using CapriHost.Core;

namespace CapriHost.Ai
{
    // NVIDIA NIM / AI Endpoints (integrate.api.nvidia.com), giao diện tương thích OpenAI.
    internal sealed class NvidiaProvider : OpenAiStyleProvider
    {
        private static readonly Regex Skip = new Regex("embed|rerank|reward|guard|safety|parse|clip|retriev|translate|ocr|asr|tts|speech|audio|image|diffusion|segment|depth|pose|classif|detect|video", RegexOptions.IgnoreCase);

        public override string Name { get { return "nvidia"; } }
        public override string Label { get { return "NVIDIA"; } }
        protected override string BaseUrl { get { return "https://integrate.api.nvidia.com/v1"; } }
        protected override string Key { get { return Settings.Current.NvidiaKey; } }
        protected override string Model { get { return Settings.Current.NvidiaModel; } }
        protected override string KeyHint { get { return "Khóa phải bắt đầu bằng nvapi-. "; } }
        protected override bool KeepModel(string id) { return !Skip.IsMatch(id); }
    }
}
