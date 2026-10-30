using System;

namespace CapriHost.Actions
{
    // Ngữ cảnh khi chạy một công cụ: ai yêu cầu và làm sao hỏi xác nhận người dùng.
    internal sealed class ToolContext
    {
        // Hỏi người dùng (trên điện thoại hoặc hộp thoại trên PC). null nghĩa là không hỏi được => từ chối.
        internal Func<string, bool> Confirm;
        // true khi người dùng tự bấm nút nhanh: coi như đã xác nhận.
        internal bool UserInitiated;
        // Báo tiến độ ("Đang mở YouTube...") để Capri hiện bong bóng cạnh sprite trên màn hình.
        internal Action<string> Progress;
        // Đã có công cụ nào chạy trong lượt này chưa (để quyết định có được chuyển sang nhà cung cấp AI dự phòng không).
        internal bool ToolsRan;
    }

    internal sealed class ToolOutcome
    {
        internal bool Ok;
        internal string Text = "";
        internal byte[] Image;          // JPEG, nếu công cụ tạo ra ảnh (chụp màn hình)

        internal static ToolOutcome Success(string text) { ToolOutcome o = new ToolOutcome(); o.Ok = true; o.Text = text; return o; }
        internal static ToolOutcome Fail(string text) { ToolOutcome o = new ToolOutcome(); o.Ok = false; o.Text = text; return o; }
    }
}
