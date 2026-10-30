using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace CapriHost.Ai
{
    // Lớp HTTP mỏng dựa trên HttpWebRequest (có từ .NET 2.0, an toàn trên Windows 7).
    internal static class Http
    {
        private static bool ready;

        // Windows 7 + .NET 4.6.1: bật TLS 1.2 một cách tường minh (Gemini/NVIDIA/ElevenLabs đều yêu cầu).
        internal static void Init()
        {
            if (ready) return;
            ready = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.DefaultConnectionLimit = 16;
        }

        internal static string Text(string method, string url, string body, Dictionary<string, string> headers, int timeoutMs)
        {
            byte[] bytes = Bytes(method, url, body, headers, timeoutMs);
            return Encoding.UTF8.GetString(bytes);
        }

        internal static byte[] Bytes(string method, string url, string body, Dictionary<string, string> headers, int timeoutMs)
        {
            Init();
            HttpWebRequest request;
            try { request = (HttpWebRequest)WebRequest.Create(url); }
            catch (UriFormatException) { throw new AiException("Địa chỉ không hợp lệ: " + url, 0, ""); }
            request.Method = method;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.UserAgent = "CapriHost/1.0";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> h in headers)
                {
                    if (h.Key == "User-Agent") request.UserAgent = h.Value;      // header bị hạn chế, phải đặt qua thuộc tính
                    else if (h.Key == "Accept") request.Accept = h.Value;
                    else request.Headers[h.Key] = h.Value;
                }
            }

            try
            {
                if (body != null)
                {
                    byte[] payload = Encoding.UTF8.GetBytes(body);
                    request.ContentType = "application/json; charset=utf-8";
                    request.ContentLength = payload.Length;
                    using (Stream stream = request.GetRequestStream()) stream.Write(payload, 0, payload.Length);
                }
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (MemoryStream copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    return copy.ToArray();
                }
            }
            catch (WebException error)
            {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response == null)
                {
                    string why = error.Status == WebExceptionStatus.Timeout ? "Hết thời gian chờ phản hồi" : error.Message;
                    if (error.Status == WebExceptionStatus.TrustFailure || error.Status == WebExceptionStatus.SecureChannelFailure)
                        why += ". Windows 7 có thể thiếu chứng chỉ gốc mới - hãy cập nhật Windows hoặc xem README mục 'Lỗi SSL'";
                    throw new AiException("Không kết nối được máy chủ: " + why, 0, "");
                }
                string text = "";
                try
                {
                    using (response)
                    using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) text = reader.ReadToEnd();
                }
                catch (IOException) { }
                throw new AiException("HTTP " + (int)response.StatusCode, (int)response.StatusCode, text);
            }
            catch (IOException error)
            {
                throw new AiException("Mất kết nối khi đang truyền dữ liệu: " + error.Message, 0, "");
            }
        }

        // Rút thông báo lỗi ngắn gọn từ JSON lỗi của nhà cung cấp.
        internal static string ErrorMessage(string body)
        {
            try
            {
                Dictionary<string, object> root = Core.Json.ReadObject(body);
                Dictionary<string, object> error = Core.Json.Obj(root, "error");
                string message = error != null ? Core.Json.Str(error, "message") : Core.Json.Str(root, "detail");
                if (!string.IsNullOrEmpty(message)) return message.Length > 220 ? message.Substring(0, 220) : message;
            }
            catch (Exception) { }
            return body != null && body.Length > 160 ? body.Substring(0, 160) : (body ?? "");
        }
    }
}
