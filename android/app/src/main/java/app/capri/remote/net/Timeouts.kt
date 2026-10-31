package app.capri.remote.net

/**
 * MỘT chỗ duy nhất chứa mọi hằng số timeout phía điện thoại (bản tương ứng của Host: host/Core/Timeouts.cs).
 * Điện thoại chờ lâu hơn Host một chút (Host tự huỷ lệnh sau 85 giây) để thông báo lỗi của Host luôn đến trước.
 */
object Timeouts {
    const val CONNECT_MS = 5_000                 // mở kết nối TCP tới Host
    const val HANDSHAKE_READ_MS = 12_000         // chờ Host trả lời lúc bắt tay
    const val IDLE_READ_MS = 30_000              // Host trả pong mỗi 10 giây; im lặng 30 giây = mất kết nối
    const val HEARTBEAT_MS = 10_000L             // chu kỳ ping
    const val RECONNECT_MAX_MS = 15_000L         // chờ tối đa giữa các lần tự kết nối lại

    const val REPLY_CHAT_MS = 90_000L            // chờ trả lời cho chat và nút nhanh
    const val REPLY_QUICK_MS = 90_000L
    const val REPLY_VOICE_MS = 120_000L          // giọng nói: thêm thời gian nhận dạng
}
