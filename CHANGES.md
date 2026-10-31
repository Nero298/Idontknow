# CHANGES — bản 1.1.0 (đợt 2)

Host 1.1.0 · giao thức v2 · Android 1.1.0 (versionCode 2). **Chưa biên dịch/chạy thử** (môi trường viết không có trình biên dịch).

## Thời gian chờ & lỗi không bị nuốt
- `Core/Timeouts.cs` + `net/Timeouts.kt`: mọi hằng số timeout ở một chỗ. HTTP có watchdog `Abort()`; mỗi lệnh có tổng 85 s; công cụ chạy trên luồng riêng (STA), hết giờ → "timeout", không dùng `Thread.Abort`.
- Mọi gói có `id` luôn nhận một `reply`; lỗi bất ngờ trong Hub/CapriServer giờ báo về điện thoại (trước đây chỉ ghi log → app quay mãi).
- **Lỗi "tab đã mở nhưng điện thoại không có phản hồi"**: lượt AI thứ hai (sau khi công cụ chạy) bị 429/lỗi thì bị coi là lỗi chung. Nay Capri trả "Đã làm xong: …" và, với "Tiết kiệm lượt AI", không gọi lượt hai khi công cụ đã OK.
- Android: bộ đếm chờ 90/120 s, mất kết nối → báo đỏ, `try/catch` quanh `handleEvent`, đọc JSON an toàn.
- Ghi stack trace, thời gian từng bước ("AI 3.2s, công cụ 0.4s, tổng 3.9s"), `Proxy=null` (bỏ dò proxy, tắt được), `SetMinThreads(32,32)`, làm sạch khóa API (ASCII in được).

## Nhiều AI
Gemini → Cloudflare Worker → Groq → LiteRouter → OpenRouter → NVIDIA (đổi thứ tự/bật tắt trong Cài đặt). Nghỉ khi 429 (`Retry-After` nếu có). Chi tiết: README mục 3.

## Dừng lệnh
`cancel {id}`, `cancel.all`, nút Dừng ở app / cửa sổ chat PC / khay hệ thống; "Đã dừng", "Gửi lại", "Sửa rồi gửi lại". Quy tắc thay thế ghi trong `Network/Hub.cs`.

## Nhật ký lỗi
Chat cố định, `log.get/log/log.push/log.clear`, vòng 300 dòng (chi tiết ≤ 1,5 KB), lọc, sao chép tất cả, chia sẻ, xoá, chấm đỏ, "Xem log" theo lệnh.

## Trình duyệt & cửa sổ
`BrowserLauncher`, `DefaultBrowser` (Win7 COM + đọc lại để kiểm tra), `window_control`, bỏ `iexplore` khỏi nhóm "edge", User-Agent Chrome 109 cho YouTube.

## Ứng dụng exe
`MainForm`, tự khởi động cùng Windows (`--autostart`), một thể hiện duy nhất.
