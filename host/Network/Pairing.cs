using System;
using System.Security.Cryptography;
using System.Text;

namespace CapriHost.Network
{
    // Ghép đôi bằng mã 10 ký tự (50 bit) hiển thị trên máy tính, nhập một lần trên điện thoại.
    // Khóa bắt tay sinh từ mã bằng PBKDF2 (20.000 vòng) nên kẻ nghe lén trong WiFi không thể dò mã dễ dàng.
    internal static class Pairing
    {
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // bỏ I, O, 0, 1 cho khỏi nhầm
        private static readonly object Gate = new object();
        private static string code;
        private static byte[] secret;
        private static DateTime expires;

        internal static event Action<string> Paired;     // tham số: tên thiết bị
        internal static event Action Changed;

        internal static bool Active
        {
            get { lock (Gate) { return code != null && DateTime.UtcNow < expires; } }
        }

        internal static string Code
        {
            get { lock (Gate) { return Active ? Show(code) : null; } }
        }

        internal static byte[] Secret
        {
            get { lock (Gate) { return Active ? secret : null; } }
        }

        internal static DateTime ExpiresAtLocal
        {
            get { lock (Gate) { return expires.ToLocalTime(); } }
        }

        // Bật chế độ ghép đôi trong 10 phút và trả về mã mới.
        internal static string Start()
        {
            byte[] random = new byte[10];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < random.Length; i++) builder.Append(Alphabet[random[i] % Alphabet.Length]);
            string fresh = builder.ToString();
            byte[] derived = Derive(fresh);
            lock (Gate)
            {
                code = fresh;
                secret = derived;
                expires = DateTime.UtcNow.AddMinutes(10);
            }
            Action changed = Changed;
            if (changed != null) changed();
            return Show(fresh);
        }

        internal static void Stop()
        {
            lock (Gate) { code = null; secret = null; }
            Action changed = Changed;
            if (changed != null) changed();
        }

        internal static void Complete(string deviceName)
        {
            Stop();
            Action<string> paired = Paired;
            if (paired != null) paired(deviceName);
        }

        // Cùng thuật toán với app Android: PBKDF2-HMAC-SHA1, salt "capri-pair", 20000 vòng, 32 byte.
        internal static byte[] Derive(string rawCode)
        {
            string normal = Normalize(rawCode);
            using (Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(normal, Encoding.UTF8.GetBytes("capri-pair"), 20000))
                return kdf.GetBytes(32);
        }

        internal static string Normalize(string text)
        {
            StringBuilder builder = new StringBuilder();
            foreach (char c in (text ?? "").ToUpperInvariant())
                if (Alphabet.IndexOf(c) >= 0) builder.Append(c);
            return builder.ToString();
        }

        private static string Show(string raw) { return raw.Substring(0, 5) + "-" + raw.Substring(5); }
    }
}
