using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using CapriHost.Core;

namespace CapriHost.Ai
{
    // Lớp HTTP mỏng dựa trên HttpWebRequest (có từ .NET 2.0, an toàn trên Windows 7).
    // Mỗi lệnh gọi có: Timeout + ReadWriteTimeout, bộ đếm giờ tổng gọi Abort() (vì DNS và dò proxy KHÔNG nằm trong
    // Timeout của HttpWebRequest), và có thể bị huỷ ngay từ CommandContext (nút Dừng).
    internal static class Http
    {
        private static bool ready;

        private sealed class State
        {
            internal volatile bool TimedOut;
        }

        // Windows 7 + .NET 4.6.1: bật TLS 1.2 một cách tường minh (Gemini/NVIDIA/Groq/OpenRouter đều yêu cầu).
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
            return Text(method, url, body, headers, timeoutMs, null);
        }

        internal static string Text(string method, string url, string body, Dictionary<string, string> headers, int timeoutMs, CommandContext cmd)
        {
            byte[] bytes = Bytes(method, url, body, headers, timeoutMs, cmd);
            return Encoding.UTF8.GetString(bytes);
        }

        internal static byte[] Bytes(string method, string url, string body, Dictionary<string, string> headers, int timeoutMs)
        {
            return Bytes(method, url, body, headers, timeoutMs, null);
        }

        internal static byte[] Bytes(string method, string url, string body, Dictionary<string, string> headers, int timeoutMs, CommandContext cmd)
        {
            Init();
            string cmdId = cmd == null ? null : cmd.Id;
            if (cmd != null)
            {
                cmd.ThrowIfCancelled();
                timeoutMs = cmd.Budget(timeoutMs);
            }

            HttpWebRequest request;
            try { request = (HttpWebRequest)WebRequest.Create(url); }
            catch (UriFormatException) { throw new AiException("Địa chỉ không hợp lệ: " + url, 0, ""); }
            string host = request.RequestUri.Host;

            request.Method = method;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.UserAgent = "CapriHost/" + Settings.Version;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (Settings.Current.BypassProxy) request.Proxy = null;     // bỏ dò proxy (chậm trên Win7)
            if (headers != null)
            {
                try
                {
                    foreach (KeyValuePair<string, string> h in headers)
                    {
                        if (h.Key == "User-Agent") request.UserAgent = h.Value;      // header bị hạn chế, phải đặt qua thuộc tính
                        else if (h.Key == "Accept") request.Accept = h.Value;
                        else request.Headers[h.Key] = h.Value;
                    }
                }
                catch (ArgumentException error)
                {
                    // Thường do khóa API có ký tự lạ (xuống dòng, ký tự vô hình).
                    throw new AiException("Khóa API có ký tự không hợp lệ (" + error.Message + "). Xoá khóa rồi dán lại trong Cài đặt.", 0, "");
                }
            }

            State state = new State();
            Stopwatch watch = Stopwatch.StartNew();
            System.Threading.Timer watchdog = new System.Threading.Timer(delegate(object o)
            {
                state.TimedOut = true;
                try { request.Abort(); }
                catch (Exception) { /* yêu cầu đã xong: không sao */ }
            }, null, timeoutMs, System.Threading.Timeout.Infinite);

            try
            {
                if (cmd != null) cmd.RegisterRequest(request);
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
                    if (watch.ElapsedMilliseconds >= 5000)
                        Log.Info("Mạng", host + " trả lời chậm: " + CommandContext.Sec(watch.ElapsedMilliseconds), cmdId);
                    return copy.ToArray();
                }
            }
            catch (WebException error)
            {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response == null) throw Translate(error, state, cmd, host, timeoutMs, error.Status);
                string text = "";
                int retryAfter = 0;
                try
                {
                    string header = response.Headers["Retry-After"];
                    if (!string.IsNullOrEmpty(header)) int.TryParse(header.Trim(), out retryAfter);
                    using (response)
                    using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) text = reader.ReadToEnd();
                }
                catch (IOException) { /* không đọc được thân lỗi: chỉ báo mã HTTP */ }
                catch (WebException) { /* như trên */ }
                if (cmd != null && cmd.Cancelled) throw new CancelledException(cmd.Reason);
                AiException failure = new AiException("HTTP " + (int)response.StatusCode, (int)response.StatusCode, text);
                failure.RetryAfterSec = retryAfter;
                throw failure;
            }
            catch (IOException error)
            {
                throw Translate(error, state, cmd, host, timeoutMs, WebExceptionStatus.ReceiveFailure);
            }
            catch (ObjectDisposedException error)
            {
                throw Translate(error, state, cmd, host, timeoutMs, WebExceptionStatus.RequestCanceled);
            }
            finally
            {
                watchdog.Dispose();
                if (cmd != null) cmd.UnregisterRequest(request);
            }
        }

        // Chuyển lỗi mạng thành CancelledException (bấm Dừng) hoặc AiException (tiếng Việt dễ hiểu).
        private static Exception Translate(Exception error, State state, CommandContext cmd, string host, int timeoutMs, WebExceptionStatus status)
        {
            if (cmd != null && cmd.Cancelled) return new CancelledException(cmd.Reason);
            if (state.TimedOut || status == WebExceptionStatus.Timeout)
            {
                return new AiException("Hết thời gian chờ " + host + " (" + (timeoutMs / 1000) + " giây). Kiểm tra mạng của máy tính; " +
                    "nếu máy phải dùng proxy thì tắt \"Bỏ qua proxy\" trong Cài đặt.", 0, "");
            }
            if (error is WebException)
            {
                string why = error.Message;
                if (status == WebExceptionStatus.NameResolutionFailure) why = "không phân giải được tên miền " + host + " (kiểm tra mạng/DNS)";
                else if (status == WebExceptionStatus.ConnectFailure) why = "không kết nối được tới " + host;
                if (status == WebExceptionStatus.TrustFailure || status == WebExceptionStatus.SecureChannelFailure)
                    why += ". Windows 7 có thể thiếu chứng chỉ gốc mới - hãy cập nhật Windows hoặc xem README mục 'Lỗi SSL'";
                return new AiException("Không kết nối được máy chủ: " + why, 0, "");
            }
            return new AiException("Mất kết nối khi đang truyền dữ liệu tới " + host + ": " + error.Message, 0, "");
        }

        // Rút thông báo lỗi ngắn gọn từ JSON lỗi của nhà cung cấp.
        internal static string ErrorMessage(string body)
        {
            try
            {
                Dictionary<string, object> root = Json.ReadObject(body);
                Dictionary<string, object> error = Json.Obj(root, "error");
                string message = error != null ? Json.Str(error, "message") : Json.Str(root, "detail");
                if (!string.IsNullOrEmpty(message)) return message.Length > 220 ? message.Substring(0, 220) : message;
            }
            catch (ArgumentException) { /* thân lỗi không phải JSON: dùng nguyên văn bên dưới */ }
            catch (InvalidOperationException) { /* như trên */ }
            return body != null && body.Length > 160 ? body.Substring(0, 160) : (body ?? "");
        }

        // Lỗi hạn mức theo ngày (không đợi vài giây là hết): dùng để quyết định nghỉ nhà cung cấp lâu hơn.
        internal static bool LooksDaily(string body)
        {
            string lower = (body ?? "").ToLowerInvariant();
            return lower.Contains("perday") || lower.Contains("per day") || lower.Contains("(rpd)") || lower.Contains("(tpd)") ||
                   lower.Contains("daily") || lower.Contains("free-tier") || lower.Contains("free_tier");
        }
    }
}
