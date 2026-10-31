# PORTING.md — những gì đã port, không port, và kiểm tra Windows 7 / 2 GB RAM

> **Quan trọng — trạng thái kiểm thử:** code được viết và rà soát tĩnh (ngoặc cân bằng, tham chiếu chéo, quét cú pháp C# 6+,
> quét API Win8+). **Chưa được biên dịch hay chạy thử trên Windows 7 thật / Android thật** (môi trường viết không có trình biên dịch
> C#, Android SDK, hay máy Win7). Lần build đầu trên GitHub Actions có thể còn vài lỗi biên dịch nhỏ — gửi log để sửa.
> Các con số RAM dưới đây là **ước tính từ thiết kế, chưa đo**.

## A. Đã port từ Capri gốc (giữ nguyên tinh thần, viết lại cho Win7)

| Thành phần gốc | Trong bản fork | Ghi chú |
|---|---|---|
| Nhân vật Capri trên màn hình (`Capri.cs`, sprite) | `Ui/Pet.cs` | Cửa sổ trong suốt từng điểm ảnh (`UpdateLayeredWindow`), kéo thả, bấm = cười (`grin`), bấm đôi = mở chat |
| Sprite: idle, blink, blink_glitch, glitch, grin, mouth, sleep, wake | `host/Assets/sprites` | Thu nhỏ còn 62,5 % (khung 150×240); nạp theo nhu cầu |
| Hành vi: chớp mắt, glitch ngẫu nhiên, ngủ sau 10 phút, thức dậy khi có lệnh | `PetForm.Tick` | Hẹn giờ 10 lần/giây, chỉ vẽ lại khi đổi khung |
| Bong bóng chữ (font Share Tech Mono, viền hồng, chữ lục lam) | `Ui/Bubble.cs` | Đặt **bên cạnh** sprite, không cướp focus |
| Tính cách Capri (`Brain.DefaultPersonality`) | `Ai/Persona.cs` | Nguyên văn + luật an toàn gốc; thêm "trả lời theo ngôn ngữ đã chọn" và độ dài câu |
| Lời chào "Good morning / Isn't it late / Miss me, choom." | `Speech/Greeting.cs` | Chỉ hiện bằng chữ (chưa có giọng) |
| Tay chân (`Hands.cs`, `Computer.cs`): mở web/app, đóng, chuột, phím, chụp màn hình | `Actions/*` | Viết lại gọn (~700 dòng thay vì ~3.200), thêm danh sách cho phép + xin xác nhận |
| Gọi NVIDIA (endpoint kiểu OpenAI) | `Ai/NvidiaProvider.cs` | Có function-calling, tự thử lại không công cụ nếu mô hình không hỗ trợ |
| Khay hệ thống, chạy nền, khởi động cùng Windows (`Startup.cs`) | `Ui/TrayApp.cs`, `Ui/MainForm.cs`, `Core/Autostart.cs` | Ứng dụng exe có cửa sổ chính; tự bật `HKCU\...\Run` ở lần chạy đầu (kèm `--autostart` để chạy êm); không cần Admin |
| Một thể hiện duy nhất, nhật ký | `Program.cs`, `Core/Log.cs` | Mở lần hai chỉ đưa cửa sổ chính lên (EventWaitHandle) |
| Giấy phép + dòng "Required Notice" | `LICENSE.md`, `NOTICE.md` | |

## B. Mới (bản gốc không có)

- **Gemini** làm LLM chính (chat, function-calling, nghe giọng → chữ, nhìn màn hình, liệt kê mô hình).
- **App Android** + giao thức LAN mã hóa, ghép đôi bằng mã, tự tìm Host (UDP), heartbeat, tự kết nối lại.
- **Nhập khóa API Gemini/NVIDIA từ app Android** (Cài đặt) — Host lưu mã hóa DPAPI, chỉ trả về "đã đặt/4 ký tự cuối".
- **Bong bóng hoạt động**: bắt lệnh → "Đang mở YouTube…" → tự mất.
- Công cụ `play_youtube`, `list_windows`, `get_active_window`, `media`, `volume`, `lock_screen`, nút nhanh chạy thẳng không qua AI.
- **Chuỗi nhiều nhà cung cấp AI** do người dùng tự sắp xếp: Gemini, Cloudflare Worker (không khóa), Groq (gpt-oss-20b), LiteRouter (DeepSeek V4.1 Flash), OpenRouter, NVIDIA; tự chuyển + nghỉ khi 429; tiết kiệm lượt AI.
- **Dừng lệnh** (`cancel`), timeout tập trung, mọi gói có `id` luôn nhận `reply`; chat **Nhật ký lỗi**.
- Công cụ `window_control` (phóng to/thu nhỏ/khôi phục/F11) và `set_default_browser`; `BrowserLauncher` (Chrome mặc định, không rơi âm thầm về IE).

## C. KHÔNG port (và lý do)

| Thành phần gốc | Lý do |
|---|---|
| `Voice.cs`, `VoicePack.cs`, `Ears.cs`, sherpa-onnx (Kokoro TTS, Parakeet ASR) | DLL native + mô hình lớn, onnxruntime không đảm bảo chạy trên Win7, tốn RAM. **Theo yêu cầu: chưa có giọng nói → chuyển thành chữ** |
| ElevenLabs (TTS + `Agent.cs` Convai realtime) | Bạn chưa có API; Convai dùng `ClientWebSocket` — **không có trên Windows 7** (cần Win8+) |
| `Dictation.cs` (nhận giọng Windows qua WinRT), đọc nhạc đang phát (SMTC) | WinRT/Windows 10+ |
| `Engine.cs` + `Brain.cs` phần LLM cục bộ (Ollama/llama) | Quá nặng cho 2 GB RAM |
| `Ledges.cs` (Capri ngồi trên mép cửa sổ/icon) | Dùng UI Automation (kéo theo assembly WPF) và tốn CPU |
| Clip: `walk`, `perch*`, `dangle`, `dance`, `arrive`, `leave` | Hoạt ảnh di chuyển/ngồi cần `Ledges`; bỏ để nhẹ (~14 MB ảnh). Thêm lại `dance/arrive/leave` rất dễ |
| `Talk.cs` (hiệu ứng toàn màn hình: Tear, Shatter, TalkPill), `Mainframe.cs` (cửa sổ cài đặt), `Setup.cs` (trình cài đặt) | Hiệu ứng overlay nặng; **cài đặt chuyển sang app Android**; không cần trình cài đặt |
| `Presence.cs` (Discord Rich Presence) | Ngoài phạm vi yêu cầu |
| `cross/` (bản Avalonia .NET 10), `site/`, `worker/` | Không liên quan Win7 / Android |
| Ghi nhớ dài hạn, thẻ `[SIGNAL CORRUPTED]` hiệu ứng riêng | Chưa làm (có thể thêm) |

## D. Kiểm tra tương thích Windows 7 SP1 (64-bit)

**Quét tự động trên mã nguồn Host** (kết quả: không có):
`WebSocket` · `HttpClient` · `async/await` · `System.Windows.Automation` · `Windows.Media/Storage` (WinRT) · `PerMonitor*` ·
`SetThreadDpi*/GetDpiFor*` · `System.Speech` · cú pháp C# 6+ (`$""`, `?.`, `nameof`, `=>` thành viên, `out var`, `when`, `{ get; } =`...).

**Win32 đang dùng (tất cả có từ Windows 2000/XP/Vista):** `keybd_event`, `mouse_event`, `SetCursorPos`, `SendInput`, `LockWorkStation`,
`GetForegroundWindow`, `GetWindowText`, `GetWindowThreadProcessId`, `EnumWindows`, `IsWindowVisible`, `IsIconic`, `IsZoomed`, `ShowWindow`, `SetForegroundWindow`, `BringWindowToTop`, `AttachThreadInput`, `GetClassName`, COM `IApplicationAssociationRegistration` (Win7), `GetDC/ReleaseDC`, `ReleaseCapture`, `SendMessage`, `UpdateLayeredWindow`,
GDI (`CreateCompatibleDC`, `SelectObject`, `DeleteObject`, `DeleteDC`), `SHEmptyRecycleBin`, `SetProcessWorkingSetSize`, winmm `waveIn*`.

**.NET dùng:** `HttpWebRequest`, `TcpListener`, `UdpClient`, `Aes` (AesCryptoServiceProvider), `Rfc2898DeriveBytes`, `HMACSHA256`,
`ProtectedData` (DPAPI), `JavaScriptSerializer`, WinForms/GDI+ — đều nằm trong .NET Framework 4.6.1 trên Win7.

| Điểm cần chú ý | Xử lý |
|---|---|
| TLS 1.2 trên Win7 | `Http.Init()` bật tường minh `Tls12 \| Tls11 \| Tls` |
| Chứng chỉ gốc cũ trên Win7 | Có thông báo lỗi rõ + hướng dẫn trong README |
| DPI | Manifest `dpiAware=true` (kiểu cũ), không dùng PerMonitorV2; sprite tự nhân theo DPI |
| Manifest `supportedOS` | Thêm GUID Win7/8/8.1/10 (bản gốc chỉ khai báo Win10) |
| Quyền | `asInvoker`, không cần Administrator; chỉ ghi `%APPDATA%\CapriHost` và `HKCU` |
| Tắt Aero (giao diện Basic) | `UpdateLayeredWindow` vẫn chạy, không dùng DWM |
| Tường lửa | Hướng dẫn `netsh` trong README (cần Admin **một lần**, tùy chọn) |
| Chưa kiểm chứng thực tế | Hành vi `UpdateLayeredWindow` + `GetHbitmap` alpha, bong bóng không cướp focus, `waveIn` trên một số driver micro cũ |

## E. Kiểm tra máy 2 GB RAM

Ước tính (chưa đo):

| Hạng mục | RAM ước tính |
|---|---|
| Runtime .NET + WinForms + tray, rảnh (sau `TrimMemory`) | ~15–30 MB |
| Sprite thường trú: `idle` (2,3 MB) + `blink` (0,6 MB) + `mouth` (0,7 MB) | ~4 MB |
| Sprite ít dùng (glitch, grin, sleep, wake, blink_glitch) | nạp khi cần, **giải phóng ngay sau khi chiếu xong** (đỉnh ~+2,3 MB) |
| Bong bóng + font | < 2 MB |
| Đỉnh tạm thời khi chụp màn hình 1080p | ~10 MB (24 bit, thu nhỏ còn 1280 px, JPEG ~150–250 KB) |
| Đỉnh tạm thời khi nhận tin nhắn thoại 60 giây | ~10–12 MB (WAV 1,9 MB → base64 → JSON) |
| **Tổng khi hoạt động bình thường** | **~35–60 MB** (Windows 7 tự nó chiếm ~0,6–0,9 GB lúc rảnh) |

**Các biện pháp đã làm:** không DLL native, không mô hình AI cục bộ (AI chạy trên đám mây); GC không chạy nền (`gcConcurrent=false`);
trả RAM cho Windows mỗi 90 giây (`GC.Collect` + `SetProcessWorkingSetSize`); sprite nạp theo nhu cầu và thu nhỏ; ảnh chụp 24 bit rồi nén JPEG;
lịch sử hội thoại tối đa 24 tin/cuộc, 40 cuộc trong RAM; khung mạng giới hạn 32 MB; hẹn giờ pet 100 ms và chỉ vẽ khi đổi khung hình;
không giữ luồng nền khi rảnh (mỗi điện thoại 1 luồng, dò tìm UDP 1 luồng).

**CPU:** rảnh gần 0 %; hoạt ảnh idle ~8 lần `UpdateLayeredWindow`/giây trên bitmap 150×240.

## F. Rủi ro / việc nên làm tiếp

1. Chạy build đầu trên Actions, gửi lỗi biên dịch (nếu có) — đặc biệt `Host` (C# 5 nghiêm ngặt) và Compose (API đổi theo phiên bản).
2. Thử trên Win7 thật: pet trong suốt, kéo thả, bong bóng, firewall, TLS tới Gemini/NVIDIA/YouTube.
3. Tên mô hình mặc định (`gemini-flash-latest`, NVIDIA) có thể đã đổi — dùng nút **Tải danh sách mô hình** trong Cài đặt.
4. `play_youtube` phụ thuộc định dạng trang YouTube (đã có phương án dự phòng).
5. Khi có API giọng nói: thêm lại TTS (ElevenLabs REST dùng HttpWebRequest chạy được Win7; **không** dùng WebSocket).
