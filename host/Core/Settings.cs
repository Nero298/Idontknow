using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CapriHost.Core
{
    // Thiết bị Android đã ghép đôi. Token dùng để sinh khóa mã hóa cho mỗi phiên.
    internal sealed class PairedDevice
    {
        internal string Id;
        internal string Name;
        internal byte[] Token;
        internal DateTime Added;
    }

    // Toàn bộ cài đặt của Capri Host. Lưu ở %APPDATA%\CapriHost\settings.json.
    // Các khóa API và token ghép đôi được mã hóa bằng DPAPI (gắn với tài khoản Windows hiện tại).
    // Phần lớn cài đặt chỉnh được từ app Android (gói tin settings.set).
    internal sealed class Settings
    {
        internal const string Version = "1.1.0";
        internal const int ProtocolVersion = 2;       // v2 = thêm cancel, log.*, reply.cmd... (xem PORTING.md); bản cũ v1 vẫn kết nối được
        internal static readonly Settings Current = new Settings();

        private static string dataDir;
        private readonly object gate = new object();
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CapriHost.v1");

        // ---- AI ----
        internal string GeminiKey = "";
        internal string NvidiaKey = "";
        internal string GeminiModel = "gemini-flash-latest";
        internal string NvidiaModel = "nvidia/nemotron-3.5-super-vl-120b-a12b";
        internal string Primary = "gemini";          // (cũ) nhà cung cấp ưu tiên; nay dùng ProviderOrder
        internal bool Failover = true;               // lỗi / hết hạn mức thì thử nhà cung cấp kế tiếp trong ProviderOrder

        // Chuỗi AI tự đổi khi hết hạn mức (429): Gemini -> Cloudflare Worker (không cần khóa) -> Groq -> OpenRouter -> NVIDIA.
        internal string ProviderOrder = "gemini,cloudflare,groq,literouter,openrouter,nvidia";   // người dùng tự sắp xếp trong Cài đặt
        internal string ProviderOff = "";            // các nhà cung cấp người dùng tắt (ngăn cách bằng dấu phẩy)
        internal string LiteRouterKey = "";
        internal string LiteRouterModel = "deepseek-v4.1-flash:free";
        internal string OpenRouterKey = "";
        internal string OpenRouterModel = "openrouter/auto";
        internal string GroqKey = "";
        internal string GroqModel = "openai/gpt-oss-20b";
        internal bool CloudflareOn = true;
        internal string CloudflareUrl = "https://ai-agent-controller.ai-agent-vy.workers.dev";
        internal bool SaveQuota = true;              // chạy công cụ xong là kết thúc, không gọi AI lần 2 (tiết kiệm lượt Gemini)

        // ---- Tính cách ----
        internal string Language = "Vietnamese";
        internal string Nickname = "";
        internal int Wordiness = 0;                  // 0 ngắn, 1 dài hơn, 2 dài
        internal string Personality = "";            // rỗng = dùng tính cách mặc định của Capri

        // ---- Lời chào (chỉ chữ, chưa có giọng nói) ----
        internal bool Greet = true;                  // Capri chào bằng bong bóng chữ khi khởi động / khi điện thoại kết nối

        // ---- Điều khiển máy ----
        internal bool AllowInput = true;             // cho phép điều khiển chuột/bàn phím
        internal string CustomApps = "";             // mỗi dòng: tên=đường dẫn
        internal bool PetVisible = true;
        internal int Port = 47821;
        internal string Browser = "chrome";          // chrome | firefox | edge | default | đường dẫn tới .exe
        internal bool BypassProxy = true;            // không dò proxy hệ thống (Win7 dò proxy rất chậm)
        internal bool FirstRunDone = false;          // đã chạy lần đầu (đã bật tự chạy cùng Windows một lần)

        internal readonly List<PairedDevice> Devices = new List<PairedDevice>();

        internal static string DataDir
        {
            get
            {
                if (dataDir == null)
                {
                    string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    dataDir = Path.Combine(root, "CapriHost");
                    try { Directory.CreateDirectory(dataDir); }
                    catch (IOException) { /* không tạo được thư mục: các lần ghi file sau sẽ báo lỗi cụ thể */ }
                    catch (UnauthorizedAccessException) { /* như trên */ }
                }
                return dataDir;
            }
        }

        private static string FilePath { get { return Path.Combine(DataDir, "settings.json"); } }

        // ---------------------------------------------------------------- lưu / đọc

        internal void Load()
        {
            lock (gate)
            {
                try
                {
                    if (!File.Exists(FilePath)) return;
                    Dictionary<string, object> map = Json.ReadObject(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (map == null) return;
                    GeminiKey = CleanKey(Open(Json.Str(map, "gemini_key", "")));
                    NvidiaKey = CleanKey(Open(Json.Str(map, "nvidia_key", "")));
                    OpenRouterKey = CleanKey(Open(Json.Str(map, "openrouter_key", "")));
                    GroqKey = CleanKey(Open(Json.Str(map, "groq_key", "")));
                    LiteRouterKey = CleanKey(Open(Json.Str(map, "literouter_key", "")));
                    LiteRouterModel = Json.Str(map, "literouter_model", LiteRouterModel);
                    ProviderOff = NormalizeOff(Json.Str(map, "provider_off", ProviderOff));
                    OpenRouterModel = Json.Str(map, "openrouter_model", OpenRouterModel);
                    GroqModel = Json.Str(map, "groq_model", GroqModel);
                    ProviderOrder = NormalizeOrder(Json.Str(map, "provider_order", ProviderOrder));
                    CloudflareOn = Json.Bool(map, "cloudflare_on", CloudflareOn);
                    CloudflareUrl = Json.Str(map, "cloudflare_url", CloudflareUrl);
                    SaveQuota = Json.Bool(map, "save_quota", SaveQuota);
                    Browser = Json.Str(map, "browser", Browser);
                    BypassProxy = Json.Bool(map, "bypass_proxy", BypassProxy);
                    FirstRunDone = Json.Bool(map, "first_run_done", FirstRunDone);
                    GeminiModel = Json.Str(map, "gemini_model", GeminiModel);
                    NvidiaModel = Json.Str(map, "nvidia_model", NvidiaModel);
                    Primary = Json.Str(map, "primary", Primary);
                    Failover = Json.Bool(map, "failover", Failover);
                    Language = Json.Str(map, "language", Language);
                    Nickname = Json.Str(map, "nickname", Nickname);
                    Wordiness = Math.Max(0, Math.Min(2, Json.Int(map, "wordiness", Wordiness)));
                    Personality = Json.Str(map, "personality", Personality);
                    Greet = Json.Bool(map, "greet", Greet);
                    AllowInput = Json.Bool(map, "allow_input", AllowInput);
                    CustomApps = Json.Str(map, "custom_apps", CustomApps);
                    PetVisible = Json.Bool(map, "pet_visible", PetVisible);
                    Port = Json.Int(map, "port", Port);
                    if (Port < 1024 || Port > 65535) Port = 47821;

                    Devices.Clear();
                    foreach (object item in Json.Arr(map, "devices"))
                    {
                        Dictionary<string, object> d = item as Dictionary<string, object>;
                        if (d == null) continue;
                        PairedDevice device = new PairedDevice();
                        device.Id = Json.Str(d, "id", "");
                        device.Name = Json.Str(d, "name", "Android");
                        string token = Open(Json.Str(d, "token", ""));
                        if (device.Id.Length == 0 || token.Length == 0) continue;
                        try { device.Token = Convert.FromBase64String(token); }
                        catch (FormatException) { continue; }
                        DateTime when;
                        device.Added = DateTime.TryParse(Json.Str(d, "added", ""), out when) ? when : DateTime.Now;
                        Devices.Add(device);
                    }
                }
                catch (Exception error)
                {
                    Log.Error("Không đọc được settings.json, dùng mặc định", error);
                }
            }
        }

        internal void Save()
        {
            lock (gate)
            {
                try
                {
                    Dictionary<string, object> map = Json.D(
                        "gemini_key", Seal(GeminiKey), "nvidia_key", Seal(NvidiaKey),
                        "openrouter_key", Seal(OpenRouterKey), "groq_key", Seal(GroqKey), "literouter_key", Seal(LiteRouterKey),
                        "literouter_model", LiteRouterModel, "provider_off", ProviderOff,
                        "openrouter_model", OpenRouterModel, "groq_model", GroqModel, "provider_order", ProviderOrder,
                        "cloudflare_on", CloudflareOn, "cloudflare_url", CloudflareUrl, "save_quota", SaveQuota,
                        "browser", Browser, "bypass_proxy", BypassProxy, "first_run_done", FirstRunDone,
                        "gemini_model", GeminiModel, "nvidia_model", NvidiaModel, "primary", Primary, "failover", Failover,
                        "language", Language, "nickname", Nickname, "wordiness", Wordiness, "personality", Personality,
                        "greet", Greet,
                        "allow_input", AllowInput, "custom_apps", CustomApps, "pet_visible", PetVisible, "port", Port);
                    List<object> devices = new List<object>();
                    foreach (PairedDevice device in Devices)
                    {
                        devices.Add(Json.D("id", device.Id, "name", device.Name,
                            "token", Seal(Convert.ToBase64String(device.Token)),
                            "added", device.Added.ToString("o")));
                    }
                    map["devices"] = devices;
                    string temp = FilePath + ".tmp";
                    File.WriteAllText(temp, Json.Write(map), Encoding.UTF8);
                    if (File.Exists(FilePath)) File.Delete(FilePath);
                    File.Move(temp, FilePath);
                }
                catch (IOException error) { Log.Error("Không lưu được settings.json", error); }
                catch (UnauthorizedAccessException error) { Log.Error("Không lưu được settings.json", error); }
            }
        }

        // ---------------------------------------------------------------- áp dụng từ điện thoại

        // Chỉ các khóa có trong "patch" mới được thay đổi. Khóa API rỗng nghĩa là xóa.
        internal void Apply(Dictionary<string, object> patch)
        {
            lock (gate)
            {
                if (Json.Has(patch, "gemini_key")) GeminiKey = CleanKey(Json.Str(patch, "gemini_key", ""));
                if (Json.Has(patch, "nvidia_key")) NvidiaKey = CleanKey(Json.Str(patch, "nvidia_key", ""));
                if (Json.Has(patch, "openrouter_key")) OpenRouterKey = CleanKey(Json.Str(patch, "openrouter_key", ""));
                if (Json.Has(patch, "groq_key")) GroqKey = CleanKey(Json.Str(patch, "groq_key", ""));
                if (Json.Has(patch, "literouter_key")) LiteRouterKey = CleanKey(Json.Str(patch, "literouter_key", ""));
                if (Json.Has(patch, "literouter_model")) LiteRouterModel = Clean(Json.Str(patch, "literouter_model"), LiteRouterModel);
                if (Json.Has(patch, "provider_off")) ProviderOff = NormalizeOff(Json.Str(patch, "provider_off", ProviderOff));
                if (Json.Has(patch, "openrouter_model")) OpenRouterModel = Clean(Json.Str(patch, "openrouter_model"), OpenRouterModel);
                if (Json.Has(patch, "groq_model")) GroqModel = Clean(Json.Str(patch, "groq_model"), GroqModel);
                if (Json.Has(patch, "provider_order")) ProviderOrder = NormalizeOrder(Json.Str(patch, "provider_order", ProviderOrder));
                if (Json.Has(patch, "cloudflare_on")) CloudflareOn = Json.Bool(patch, "cloudflare_on", CloudflareOn);
                if (Json.Has(patch, "cloudflare_url")) CloudflareUrl = Clean(Json.Str(patch, "cloudflare_url"), CloudflareUrl);
                if (Json.Has(patch, "save_quota")) SaveQuota = Json.Bool(patch, "save_quota", SaveQuota);
                if (Json.Has(patch, "browser")) Browser = Clean(Json.Str(patch, "browser"), "chrome").Trim('"');
                if (Json.Has(patch, "bypass_proxy")) BypassProxy = Json.Bool(patch, "bypass_proxy", BypassProxy);
                if (Json.Has(patch, "gemini_model")) GeminiModel = Clean(Json.Str(patch, "gemini_model"), GeminiModel);
                if (Json.Has(patch, "nvidia_model")) NvidiaModel = Clean(Json.Str(patch, "nvidia_model"), NvidiaModel);
                if (Json.Has(patch, "primary"))
                {
                    // Tương thích app cũ: "primary" đưa nhà cung cấp đó lên đầu chuỗi.
                    Primary = Json.Str(patch, "primary") == "nvidia" ? "nvidia" : "gemini";
                    ProviderOrder = NormalizeOrder(Primary + "," + ProviderOrder);
                }
                if (Json.Has(patch, "failover")) Failover = Json.Bool(patch, "failover", Failover);
                if (Json.Has(patch, "language")) Language = Clean(Json.Str(patch, "language"), "Vietnamese");
                if (Json.Has(patch, "nickname")) Nickname = (Json.Str(patch, "nickname", "") ?? "").Trim();
                if (Json.Has(patch, "wordiness")) Wordiness = Math.Max(0, Math.Min(2, Json.Int(patch, "wordiness", Wordiness)));
                if (Json.Has(patch, "personality")) Personality = Json.Str(patch, "personality", "") ?? "";
                if (Json.Has(patch, "greet")) Greet = Json.Bool(patch, "greet", Greet);
                if (Json.Has(patch, "allow_input")) AllowInput = Json.Bool(patch, "allow_input", AllowInput);
                if (Json.Has(patch, "custom_apps")) CustomApps = Json.Str(patch, "custom_apps", "") ?? "";
                if (Json.Has(patch, "pet_visible")) PetVisible = Json.Bool(patch, "pet_visible", PetVisible);
            }
            Save();
        }

        // Bản gửi cho điện thoại: không bao giờ chứa khóa đầy đủ, chỉ có "đã đặt chưa" và 4 ký tự cuối.
        internal Dictionary<string, object> ToPublic()
        {
            lock (gate)
            {
                return Json.D(
                    "gemini_key_set", GeminiKey.Length > 0, "gemini_key_hint", Hint(GeminiKey),
                    "nvidia_key_set", NvidiaKey.Length > 0, "nvidia_key_hint", Hint(NvidiaKey),
                    "openrouter_key_set", OpenRouterKey.Length > 0, "openrouter_key_hint", Hint(OpenRouterKey),
                    "groq_key_set", GroqKey.Length > 0, "groq_key_hint", Hint(GroqKey),
                    "literouter_key_set", LiteRouterKey.Length > 0, "literouter_key_hint", Hint(LiteRouterKey),
                    "literouter_model", LiteRouterModel, "provider_off", ProviderOff,
                    "openrouter_model", OpenRouterModel, "groq_model", GroqModel, "provider_order", ProviderOrder,
                    "cloudflare_on", CloudflareOn, "cloudflare_url", CloudflareUrl, "save_quota", SaveQuota,
                    "browser", Browser, "bypass_proxy", BypassProxy,
                    "gemini_model", GeminiModel, "nvidia_model", NvidiaModel, "primary", Primary, "failover", Failover,
                    "language", Language, "nickname", Nickname, "wordiness", Wordiness, "personality", Personality,
                    "greet", Greet,
                    "allow_input", AllowInput, "custom_apps", CustomApps, "pet_visible", PetVisible,
                    "version", Version, "devices", Devices.Count);
            }
        }

        // ---------------------------------------------------------------- thiết bị đã ghép đôi

        internal PairedDevice FindDevice(string id)
        {
            lock (gate)
            {
                foreach (PairedDevice device in Devices) if (device.Id == id) return device;
                return null;
            }
        }

        internal void AddDevice(string id, string name, byte[] token)
        {
            lock (gate)
            {
                Devices.RemoveAll(delegate(PairedDevice d) { return d.Id == id; });
                PairedDevice device = new PairedDevice();
                device.Id = id;
                device.Name = name;
                device.Token = token;
                device.Added = DateTime.Now;
                Devices.Add(device);
            }
            Save();
        }

        internal void RemoveAllDevices()
        {
            lock (gate) { Devices.Clear(); }
            Save();
        }

        // ---------------------------------------------------------------- tiện ích

        // Khóa API chỉ gồm ký tự ASCII in được (không khoảng trắng). Ký tự vô hình (zero-width, NBSP, BOM...)
        // dán nhầm từ điện thoại sẽ làm HttpWebRequest ném ArgumentException khi đặt header.
        internal static string CleanKey(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            StringBuilder builder = new StringBuilder(raw.Length);
            foreach (char c in raw)
                if (c > ' ' && c < (char)0x7F) builder.Append(c);
            return builder.ToString();
        }

        internal static readonly string[] AllProviders = { "gemini", "cloudflare", "groq", "literouter", "openrouter", "nvidia" };

        // Chuỗi thứ tự: bỏ trùng, bỏ tên lạ, thêm các nhà còn thiếu vào cuối.
        internal static string NormalizeOrder(string raw)
        {
            List<string> result = new List<string>();
            foreach (string part in (raw ?? "").Split(','))
            {
                string name = part.Trim().ToLowerInvariant();
                if (Array.IndexOf(AllProviders, name) >= 0 && !result.Contains(name)) result.Add(name);
            }
            foreach (string name in AllProviders) if (!result.Contains(name)) result.Add(name);
            return string.Join(",", result.ToArray());
        }

        internal static string NormalizeOff(string raw)
        {
            List<string> result = new List<string>();
            foreach (string part in (raw ?? "").Split(','))
            {
                string name = part.Trim().ToLowerInvariant();
                if (Array.IndexOf(AllProviders, name) >= 0 && !result.Contains(name)) result.Add(name);
            }
            return string.Join(",", result.ToArray());
        }

        internal bool IsProviderOff(string name)
        {
            return Array.IndexOf((ProviderOff ?? "").Split(','), name) >= 0;
        }

        private static string Clean(string value, string fallback)
        {
            if (value == null) return fallback;
            value = value.Trim();
            return value.Length == 0 ? fallback : value;
        }

        private static string Hint(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return key.Length <= 8 ? "••••" : "…" + key.Substring(key.Length - 4);
        }

        private static string Seal(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            byte[] bytes = Encoding.UTF8.GetBytes(plain);
            try { return "dp:" + Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser)); }
            catch (CryptographicException) { return "pl:" + Convert.ToBase64String(bytes); }
        }

        private static string Open(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            try
            {
                if (stored.StartsWith("dp:", StringComparison.Ordinal))
                    return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(3)), Entropy, DataProtectionScope.CurrentUser));
                if (stored.StartsWith("pl:", StringComparison.Ordinal))
                    return Encoding.UTF8.GetString(Convert.FromBase64String(stored.Substring(3)));
            }
            catch (CryptographicException) { return ""; }
            catch (FormatException) { return ""; }
            return stored;
        }
    }
}
