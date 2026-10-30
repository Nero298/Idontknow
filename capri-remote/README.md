# Capri Remote — Capri Host (Windows 7) + Capri (Android)

Bản fork của **Capri Desktop** chia thành hai ứng dụng:

| | Ứng dụng | Công nghệ | Chạy trên |
|---|---|---|---|
| 1 | **Capri Host** (`CapriHost.exe`) — "bộ não + tay chân" | C# 5, .NET Framework 4.6.1, WinForms | Windows 7 SP1 64-bit trở lên, RAM ≥ 2 GB |
| 2 | **Capri** (`capri-remote.apk`) — điều khiển từ xa | Kotlin, Jetpack Compose (giao diện kiểu Claude) | Android 13+ (minSdk 33, targetSdk 36) |

Giấy phép: PolyForm Noncommercial 1.0.0 (chỉ phi thương mại) — xem `LICENSE.md` và `NOTICE.md`.
Danh sách phần đã port / bỏ / mới và kiểm tra Windows 7 + 2 GB RAM: **`PORTING.md`**.

## Cách hoạt động

```
Điện thoại (gõ lệnh) ──TCP + JSON, mã hóa AES/HMAC, cùng WiFi──▶ Capri Host
                                                               │ 1. "bắt" lệnh  → bong bóng cạnh Capri: "Nhận lệnh: ..."
                                                               │ 2. AI (Gemini → NVIDIA dự phòng) chọn công cụ
                                                               │ 3. chạy công cụ → bong bóng: "Đang mở YouTube: Love Me Not"
                                                               │ 4. trả kết quả (chữ) về điện thoại, bong bóng tự biến mất
```

Ví dụ gõ trên điện thoại: *"bật bài Love Me Not trên YouTube bằng chrome"* → Capri trên màn hình giật nhẹ,
hiện bong bóng nhỏ bên cạnh ("Nhận lệnh…" → "Đang mở YouTube: Love Me Not" → "Xong rồi") rồi mất.

**Chưa có giọng nói**: mọi phản hồi của Capri là chữ. Nút mic trên điện thoại vẫn có: ghi âm → Host nhờ
Gemini chuyển thành chữ → xử lý như lệnh gõ (cần khóa Gemini). Không có mô hình giọng nói nào chạy trên máy.

## 1. Build trên GitHub Actions

1. Tạo repo GitHub, đẩy toàn bộ thư mục này lên nhánh `main`.
2. Tab **Actions** → workflow **Build Capri** tự chạy (hoặc bấm *Run workflow*).
3. Tải hai file ở mục *Artifacts*: `CapriHost-win7` (zip chứa `CapriHost.exe`) và `capri-remote-apk`.
4. Muốn tạo bản phát hành: `git tag v1.0.0 && git push --tags` → Actions đính kèm cả hai file vào *Releases*.

Host được biên dịch với `LangVersion=5` nên **lỡ viết C# 6+ là build đỏ ngay**. APK ký bằng khóa debug để cài thẳng được
(muốn phát hành lâu dài thì thay `signingConfig` trong `android/app/build.gradle.kts`).

Build ở máy: `dotnet build host/CapriHost.csproj -c Release` (Windows/Linux/macOS đều được) và
`gradle -p android :app:assembleRelease` (JDK 17, Gradle 8.13, Android SDK 36).

## 2. Cài Capri Host trên Windows 7

1. Windows 7 **SP1** + **.NET Framework 4.6.1** (bản mới hơn như 4.7.2/4.8 cũng được). Nếu bộ cài .NET báo lỗi chữ ký số,
   cài trước bản cập nhật SHA-2 cho Windows 7 (KB4474419) rồi cài lại.
2. Giải nén `CapriHost-win7.zip` vào một thư mục bất kỳ (không cần quyền Administrator), chạy `CapriHost.exe`.
3. Lần đầu, Windows Firewall sẽ hỏi: chọn **Mạng riêng (Private)** → Cho phép. Nếu điện thoại không kết nối được, mở
   *Command Prompt (Admin)* chạy một lần:
   ```
   netsh advfirewall firewall add rule name="Capri Host" dir=in action=allow protocol=TCP localport=47821
   netsh advfirewall firewall add rule name="Capri Host discover" dir=in action=allow protocol=UDP localport=47822
   ```
4. Capri hiện ở góc dưới bên phải, biểu tượng ở khay hệ thống. Chuột phải để mở menu; bấm đôi vào Capri để chat ngay trên PC.
5. Dữ liệu (cài đặt mã hóa DPAPI, nhật ký): `%APPDATA%\CapriHost\`.

**Lỗi SSL / "Không kết nối được máy chủ" trên Win7:** Win7 cũ thiếu chứng chỉ gốc mới (Google/NVIDIA dùng). Chạy Windows Update
(mục *Root Certificates*), hoặc cài thủ công chứng chỉ *GTS Root R1* và *ISRG Root X1* vào "Trusted Root Certification Authorities".
Host đã tự bật TLS 1.2.

## 3. Cài app Android và ghép đôi

1. Cài `capri-remote.apk` (cho phép cài từ nguồn không xác định).
2. Trên máy tính: menu khay → **Ghép đôi điện thoại mới…** → hiện **mã 10 ký tự** và địa chỉ IP.
3. Trên điện thoại (cùng WiFi): bấm **Tìm Capri trong mạng** (hoặc nhập IP) → nhập mã → **Ghép đôi và kết nối**.
   Mã chỉ cần một lần; sau đó điện thoại giữ token riêng.
4. Vào **Cài đặt → Gemini / NVIDIA**: dán khóa API, bấm **Tải danh sách mô hình** để chọn mô hình (tên mô hình đổi thường xuyên
   nên luôn lấy từ API), rồi **Lưu vào máy tính**. Khóa được Host mã hóa bằng DPAPI, **không bao giờ gửi ngược lại đầy đủ** và không lưu trên điện thoại.

Khóa lấy ở: Google AI Studio (Gemini) và build.nvidia.com (NVIDIA, dạng `nvapi-…`).

## 4. Công cụ Capri điều khiển được

Mở website · **Bật nhạc/video YouTube** (tìm và mở kết quả đầu tiên, có thể chỉ định Chrome) · Mở/đóng ứng dụng · Liệt kê cửa sổ ·
Dọn dẹp (`%TEMP%` + thùng rác, luôn xin xác nhận) · Bấm phím tắt · Gõ chữ · Chuột (di/bấm/cuộn) · Chụp màn hình (gửi về điện thoại) ·
Nhìn màn hình và trả lời (Gemini) · Nhạc/âm lượng · Khoá máy. Nút nhanh trên điện thoại chạy thẳng công cụ, không tốn lượt AI.

Lưu ý YouTube: Host tải trang kết quả và lấy video đầu tiên (cách "tốt nhất có thể"; nếu YouTube đổi trang thì mở trang kết quả tìm kiếm).
Trình duyệt có thể chặn tự phát — khi đó nói "tạm dừng/phát nhạc" hoặc bấm phát một lần.

## 5. Cấu trúc thư mục

```
host/                    Capri Host (net461, C# 5)
  Core/       Settings (DPAPI), Json, Log, Native (P/Invoke có từ Win2000/XP)
  Network/    SecureChannel, CapriServer, Pairing, Discovery (UDP), Hub (điều phối)
  Ai/         Agent (vòng lặp AI+công cụ), GeminiProvider, NvidiaProvider, Persona, Http
  Actions/    Tools, AppLauncher, Cleaner, InputControl, ScreenTools, YouTube
  Speech/     Greeting (chữ), Mic (ghi âm → Gemini chuyển chữ)
  Ui/         PetForm (sprite + bong bóng), Bubble, ChatBox, PairForm, TrayApp
  Assets/     sprites (đã thu nhỏ), font, icon
android/                 Capri Remote (Kotlin, Compose)
  net/        Crypto, SecureSocket, CapriClient (heartbeat + reconnect), Discovery
  data/       Prefs, Chats (lịch sử)
  audio/      Recorder
  ui/         Theme, AppViewModel, ChatScreen, ConnectScreen, SettingsScreen, Components
.github/workflows/build.yml
```

## 6. Giao thức (tóm tắt)

TCP cổng **47821**; UDP dò tìm cổng **47822**. Khung: `[4 byte độ dài][payload]`. Bắt tay `hello`/`hello_ok` dạng JSON thường (có nonce),
sau đó mọi gói là `iv(16) | AES-256-CBC | HMAC-SHA256(32)` (HMAC phủ hướng + số thứ tự + iv + ciphertext).
Khóa phiên = HMAC(secret, "capri1"|nonce_c|nonce_s); `secret` là token đã ghép đôi hoặc PBKDF2 (20 000 vòng) của mã ghép đôi.
Ping 10 giây/lần; client tự kết nối lại (1 → 15 giây). Sai khóa 5 lần thì Host chặn IP đó 5 phút.
Gói: `chat`, `voice`, `quick`, `settings.get/set`, `models.list`, `confirm.reply` ↔ `reply`, `transcript`, `confirm`, `settings`, `models`.
