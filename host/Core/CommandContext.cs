using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace CapriHost.Core
{
    // Ném ra khi lệnh bị huỷ (bấm Dừng, hết giờ, điện thoại mất kết nối, bị lệnh mới thay).
    internal sealed class CancelledException : Exception
    {
        internal CancelledException(string reason) : base(reason) { }
    }

    // Ngữ cảnh của MỘT lệnh: id, cờ huỷ, các HttpWebRequest đang chạy (để Abort() ngay),
    // đồng hồ đo thời gian từng bước, và bộ đếm giờ tổng.
    internal sealed class CommandContext
    {
        internal readonly string Id;          // trùng id tin nhắn trên điện thoại
        internal readonly string Conv;
        internal readonly string Kind;        // chat | voice | quick
        internal readonly object Owner;       // ClientSession; null nếu lệnh từ chính PC
        internal readonly Stopwatch Clock = Stopwatch.StartNew();
        internal readonly ManualResetEvent CancelHandle = new ManualResetEvent(false);
        internal volatile bool TimedOut;

        private readonly object gate = new object();
        private readonly List<HttpWebRequest> requests = new List<HttpWebRequest>();
        private volatile bool cancelled;
        private string reason = "";
        private System.Threading.Timer watchdog;
        private long aiMs;
        private long toolMs;
        private int aiCalls;
        private int toolCalls;
        private long pausedMs;
        private long pauseStartedAt = -1;
        internal string AiProvider = "";      // nhà cung cấp AI đã trả lời cuối cùng
        internal int AiCountToday;            // số lượt AI hôm nay của nhà cung cấp đó (Capri tự đếm)

        internal CommandContext(string id, string conv, string kind, object owner)
        {
            Id = id ?? "";
            Conv = conv ?? "";
            Kind = kind ?? "chat";
            Owner = owner;
        }

        internal bool Cancelled { get { return cancelled; } }

        private int replied;

        // Mỗi lệnh chỉ được trả lời ĐÚNG MỘT LẦN (kể cả khi vừa bị huỷ vừa chạy xong). Trả về true cho lần gọi đầu tiên.
        internal bool TryReply()
        {
            return Interlocked.CompareExchange(ref replied, 1, 0) == 0;
        }

        internal bool Replied { get { return replied != 0; } }

        // Đặt lại giờ tổng sau khi chờ xác nhận: thời gian chờ người dùng không tính vào hạn của lệnh.
        internal void Rearm()
        {
            long used = Clock.ElapsedMilliseconds - PausedNowMs();
            long left = Timeouts.CommandMs - used;
            if (left < 5000) left = 5000;
            Disarm();
            if (!cancelled) Arm((int)left);
        }

        internal string Reason
        {
            get { lock (gate) { return reason; } }
        }

        // Đặt giờ tổng cho lệnh: hết giờ thì tự huỷ và đánh dấu TimedOut.
        internal void Arm(int ms)
        {
            lock (gate)
            {
                if (watchdog != null) return;
                watchdog = new System.Threading.Timer(delegate(object state)
                {
                    TimedOut = true;
                    Cancel("timeout");
                }, null, ms, System.Threading.Timeout.Infinite);
            }
        }

        // Dừng bộ đếm giờ (khi lệnh đã xong).
        internal void Disarm()
        {
            lock (gate)
            {
                if (watchdog != null) { watchdog.Dispose(); watchdog = null; }
            }
        }

        // Huỷ lệnh: đặt cờ, đánh thức mọi chỗ đang chờ, Abort() mọi HTTP đang chạy. Trả về false nếu đã huỷ từ trước.
        internal bool Cancel(string why)
        {
            List<HttpWebRequest> copy;
            lock (gate)
            {
                if (cancelled) return false;
                cancelled = true;
                reason = why ?? "";
                copy = new List<HttpWebRequest>(requests);
            }
            try { CancelHandle.Set(); }
            catch (ObjectDisposedException) { /* không ai còn chờ */ }
            foreach (HttpWebRequest request in copy)
            {
                try { request.Abort(); }
                catch (Exception) { /* yêu cầu đã xong/đã đóng: không sao */ }
            }
            return true;
        }

        internal void ThrowIfCancelled()
        {
            if (cancelled) throw new CancelledException(Reason);
        }

        internal void RegisterRequest(HttpWebRequest request)
        {
            bool abortNow;
            lock (gate)
            {
                requests.Add(request);
                abortNow = cancelled;
            }
            if (abortNow)
            {
                try { request.Abort(); }
                catch (Exception) { /* như trên */ }
            }
        }

        internal void UnregisterRequest(HttpWebRequest request)
        {
            lock (gate) { requests.Remove(request); }
        }

        // Thời gian tối đa cho một lệnh gọi HTTP: không vượt quá phần còn lại của lệnh (nhưng tối thiểu 2 giây).
        internal int Budget(int capMs)
        {
            long left = Timeouts.CommandMs - (Clock.ElapsedMilliseconds - PausedNowMs());
            if (left < 2000) left = 2000;
            return (int)Math.Min(capMs, left);
        }

        // ---- đo thời gian
        internal void AddAi(long ms) { lock (gate) { aiMs += ms; aiCalls++; } }
        internal void AddTool(long ms) { lock (gate) { toolMs += ms; toolCalls++; } }

        internal string Summary()
        {
            lock (gate)
            {
                string ai = "AI " + Sec(aiMs) + (aiCalls > 1 ? " (" + aiCalls + " lượt)" : "");
                if (AiProvider.Length > 0)
                    ai += " qua " + AiProvider + (AiCountToday > 0 ? " #" + AiCountToday + " hôm nay" : "");
                return ai + ", công cụ " + Sec(toolMs) + (toolCalls > 1 ? " (" + toolCalls + " lần)" : "") + ", tổng " + Sec(Clock.ElapsedMilliseconds);
            }
        }

        internal static string Sec(long ms)
        {
            return (ms / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
        }

        // ---- thời gian "tạm dừng" (chờ người dùng xác nhận) không tính vào hạn của công cụ.
        internal void BeginPause()
        {
            lock (gate) { if (pauseStartedAt < 0) pauseStartedAt = Clock.ElapsedMilliseconds; }
        }

        internal void EndPause()
        {
            lock (gate)
            {
                if (pauseStartedAt >= 0) { pausedMs += Clock.ElapsedMilliseconds - pauseStartedAt; pauseStartedAt = -1; }
            }
        }

        internal long PausedNowMs()
        {
            lock (gate)
            {
                return pausedMs + (pauseStartedAt >= 0 ? Clock.ElapsedMilliseconds - pauseStartedAt : 0);
            }
        }
    }
}
