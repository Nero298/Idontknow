namespace CapriHost.Core
{
    // MỘT chỗ duy nhất chứa mọi hằng số timeout của Host. Muốn chỉnh thì sửa ở đây.
    // (Phía Android có bản tương ứng ở net/Timeouts.kt; giữ hai bên khớp nhau:
    //  điện thoại chờ lâu hơn Host một chút để lỗi của Host luôn đến trước.)
    internal static class Timeouts
    {
        // ---- HTTP (AI / YouTube). Tổng thời gian tối đa của MỘT lệnh gọi, kể cả DNS và dò proxy
        //      (HttpWebRequest.Timeout không bao gồm hai bước đó nên Http.cs có thêm bộ đếm giờ gọi Abort()).
        internal const int HttpMs = 30000;
        internal const int HttpMediaMs = 35000;       // tải âm thanh / ảnh màn hình lên Gemini (gói lớn hơn)
        internal const int HttpListModelsMs = 20000;  // danh sách mô hình
        internal const int HttpYouTubeMs = 8000;      // tải trang kết quả YouTube

        // ---- Một lệnh (cả vòng AI + công cụ). Quá hạn thì huỷ và báo lỗi.
        //      Nhỏ hơn 90 giây phía Android để thông báo lỗi của Host luôn đến trước.
        internal const int CommandMs = 85000;

        // ---- Công cụ: chạy trên luồng riêng, chờ có hạn.
        internal const int ToolDefaultMs = 15000;
        internal const int ToolCleanupMs = 60000;
        internal const int ToolLookMs = 40000;
        internal const int ToolYouTubeMs = 15000;
        internal const int ToolDefaultBrowserMs = 25000;

        // ---- Xác nhận từ điện thoại: hết hạn coi là "Không".
        internal const int ConfirmMs = 60000;

        // ---- TCP với điện thoại.
        internal const int TcpHandshakeMs = 10000;    // bắt tay
        internal const int TcpWriteMs = 20000;
        internal const int TcpIdleMs = 45000;         // điện thoại ping mỗi 10 giây; im lặng 45 giây = mất kết nối

        internal static int ForTool(string name)
        {
            switch (name)
            {
                case "cleanup": return ToolCleanupMs;
                case "look_at_screen": return ToolLookMs;
                case "play_youtube": return ToolYouTubeMs;
                case "set_default_browser": return ToolDefaultBrowserMs;
            }
            return ToolDefaultMs;
        }
    }
}
