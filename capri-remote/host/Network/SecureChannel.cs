using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace CapriHost.Network
{
    internal sealed class AuthException : Exception
    {
        internal AuthException(string message) : base(message) { }
    }

    // Khung TCP: [4 byte độ dài big-endian][payload].
    //  - Gói bắt tay (hello / hello_ok) là JSON thường.
    //  - Sau đó mọi gói là:  iv(16) | AES-256-CBC(JSON) | HMAC-SHA256(32)
    //    HMAC phủ: hướng(1) | số thứ tự(8) | iv | ciphertext  => chống sửa gói, phát lại, đảo thứ tự.
    // Khóa phiên = HMAC(secret, "capri1" | clientNonce | serverNonce), secret là token đã ghép đôi
    // (hoặc khóa dẫn xuất từ mã ghép đôi khi đang ghép đôi). Mật khẩu/khóa API không bao giờ đi dạng chữ thường.
    internal sealed class SecureChannel
    {
        internal const int MaxFrame = 32 * 1024 * 1024;
        private readonly NetworkStream stream;
        private readonly object sendLock = new object();
        private byte[] encKey;
        private byte[] macKey;
        private long sendSeq;
        private long recvSeq;

        // Host gửi hướng 1 (server→client), nhận hướng 0 (client→server).
        private const byte SendDir = 1;
        private const byte RecvDir = 0;

        internal SecureChannel(NetworkStream stream) { this.stream = stream; }

        // ---------------------------------------------------------------- bắt tay (không mã hóa)

        internal void SendPlain(string json) { WriteFrame(Encoding.UTF8.GetBytes(json)); }

        internal string ReadPlain() { return Encoding.UTF8.GetString(ReadFrame()); }

        internal void Arm(byte[] secret, byte[] clientNonce, byte[] serverNonce)
        {
            byte[] material = Concat(Encoding.ASCII.GetBytes("capri1"), clientNonce, serverNonce);
            byte[] key;
            using (HMACSHA256 h = new HMACSHA256(secret)) key = h.ComputeHash(material);
            using (HMACSHA256 h = new HMACSHA256(key))
            {
                encKey = h.ComputeHash(Encoding.ASCII.GetBytes("enc"));
                macKey = h.ComputeHash(Encoding.ASCII.GetBytes("mac"));
            }
        }

        // ---------------------------------------------------------------- gói mã hóa

        internal void Send(string json)
        {
            byte[] plain = Encoding.UTF8.GetBytes(json);
            lock (sendLock)
            {
                byte[] iv = new byte[16];
                byte[] cipher;
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(iv);
                using (Aes aes = Aes.Create())
                {
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = encKey;
                    aes.IV = iv;
                    using (ICryptoTransform encryptor = aes.CreateEncryptor()) cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                }
                byte[] mac = Mac(SendDir, sendSeq, iv, cipher);
                sendSeq++;
                WriteFrame(Concat(iv, cipher, mac));
            }
        }

        internal string Read()
        {
            byte[] frame = ReadFrame();
            if (frame.Length < 16 + 16 + 32) throw new AuthException("Gói tin quá ngắn");
            byte[] iv = new byte[16];
            byte[] mac = new byte[32];
            byte[] cipher = new byte[frame.Length - 48];
            Buffer.BlockCopy(frame, 0, iv, 0, 16);
            Buffer.BlockCopy(frame, 16, cipher, 0, cipher.Length);
            Buffer.BlockCopy(frame, 16 + cipher.Length, mac, 0, 32);
            if (!Same(mac, Mac(RecvDir, recvSeq, iv, cipher))) throw new AuthException("Sai khóa hoặc gói bị sửa");
            recvSeq++;
            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = encKey;
                    aes.IV = iv;
                    using (ICryptoTransform decryptor = aes.CreateDecryptor())
                        return Encoding.UTF8.GetString(decryptor.TransformFinalBlock(cipher, 0, cipher.Length));
                }
            }
            catch (CryptographicException) { throw new AuthException("Không giải mã được"); }
        }

        // ---------------------------------------------------------------- khung thô

        private void WriteFrame(byte[] payload)
        {
            byte[] head = new byte[4];
            head[0] = (byte)(payload.Length >> 24);
            head[1] = (byte)(payload.Length >> 16);
            head[2] = (byte)(payload.Length >> 8);
            head[3] = (byte)payload.Length;
            lock (stream)
            {
                stream.Write(head, 0, 4);
                stream.Write(payload, 0, payload.Length);
                stream.Flush();
            }
        }

        private byte[] ReadFrame()
        {
            byte[] head = ReadExact(4);
            int length = (head[0] << 24) | (head[1] << 16) | (head[2] << 8) | head[3];
            if (length < 0 || length > MaxFrame) throw new IOException("Gói tin không hợp lệ");
            return ReadExact(length);
        }

        private byte[] ReadExact(int count)
        {
            byte[] buffer = new byte[count];
            int done = 0;
            while (done < count)
            {
                int n = stream.Read(buffer, done, count - done);
                if (n <= 0) throw new IOException("Kết nối đã đóng");
                done += n;
            }
            return buffer;
        }

        private byte[] Mac(byte dir, long seq, byte[] iv, byte[] cipher)
        {
            byte[] head = new byte[9];
            head[0] = dir;
            for (int i = 0; i < 8; i++) head[1 + i] = (byte)(seq >> (56 - 8 * i));
            using (HMACSHA256 h = new HMACSHA256(macKey)) return h.ComputeHash(Concat(head, iv, cipher));
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static byte[] Concat(params byte[][] parts)
        {
            int total = 0;
            foreach (byte[] part in parts) total += part.Length;
            byte[] all = new byte[total];
            int at = 0;
            foreach (byte[] part in parts) { Buffer.BlockCopy(part, 0, all, at, part.Length); at += part.Length; }
            return all;
        }
    }
}
