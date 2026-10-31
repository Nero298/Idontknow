# Capri Remote — Capri Host (Windows 7) + Capri (Android)

Bản **1.1.0** — fork của **Capri Desktop** chia thành hai ứng dụng:

| | Ứng dụng | Công nghệ | Chạy trên |
|---|---|---|---|
| 1 | **Capri Host** (`CapriHost.exe`) — "bộ não + tay chân" | C# 5, .NET Framework 4.6.1, WinForms | Windows 7 SP1 64-bit trở lên, RAM ≥ 2 GB |
| 2 | **Capri** (`capri-remote.apk`) — điều khiển từ xa | Kotlin, Jetpack Compose (giao diện kiểu Claude) | Android 13+ (minSdk 33, targetSdk 36) |

Giấy phép: PolyForm Noncommercial 1.0.0 (chỉ phi thương mại) — xem `LICENSE.md` và `NOTICE.md`.
Danh sách phần đã port / bỏ / mới và kiểm tra Windows 7 + 2 GB RAM: **`PORTING.md`**. Thay đổi bản 1.1.0: **`CHANGES.md`**.

## Cách hoạt động

```
Điện thoại (gõ lệnh) ──TCP + JSON, mã hóa AES/HMAC, cùng WiFi──▶ Capri Host
                                                               │ 1. "bắt" lệnh  → bong bóng cạnh Capri: "Nhận lệnh: ..."
                                                               │ 2. AI (thứ tự bạn tự sắp xếp, mặc định Gemini → Cloudflare Worker → Groq → LiteRouter → OpenRouter → NVIDIA) chọn công cụ
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
3. Tải ở mục *Artifacts*: `CapriHost-exe` (file `CapriHost.exe` chạy thẳng), `CapriHost-win7` (zip kèm giấy phép) và `capri-remote-apk`.
   (Nếu GitHub không cho dùng thư mục ẩn `.github`, dùng file `build.yml` ở thư mục gốc: chép nó vào `.github/workflows/` bằng giao diện web của GitHub.)
4. Muốn tạo bản phát hành: `git tag v1.1.0 && git push --tags` → Actions đính kèm cả hai file vào *Releases*.

Host được biên dịch với `LangVersion=5` nên **lỡ viết C# 6+ là build đỏ ngay**. APK ký bằng khóa debug để cài thẳng được
(muốn phát hành lâu dài thì thay `signingConfig` trong `android/app/build.gradle.kts`).

Build ở máy: `dotnet build host/CapriHost.csproj -c Release` (Windows/Linux/macOS đều được) và
`gradle -p android :app:assembleRelease` (JDK 17, Gradle 8.13, Android SDK 36).

## 2. Cài Capri Host trên Windows 7

1. Windows 7 **SP1** + **.NET Framework 4.6.1** (bản mới hơn như 4.7.2/4.8 cũng được). Nếu bộ cài .NET báo lỗi chữ ký số,
   cài trước bản cập nhật SHA-2 cho Windows 7 (KB4474419) rồi cài lại.
2. Chép `CapriHost.exe` vào một thư mục cố định (ví dụ `C:\Capri`; không cần quyền Administrator) rồi **chạy một lần**.
   Capri mở **cửa sổ chính** (trạng thái, địa chỉ kết nối, thứ tự AI, nhật ký trực tiếp). **Lần đầu chạy, Capri tự bật "Khởi động cùng Windows"**:
   từ lần sau, mở máy là Capri tự chạy êm ở khay hệ thống (tham số `--autostart`, không bật cửa sổ). Tắt/bật lại bằng ô
   "Khởi động cùng Windows" trong cửa sổ chính hoặc menu khay. Bấm X chỉ thu xuống khay; muốn tắt hẳn chọn **Thoát hẳn**.
   Mở `CapriHost.exe` lần nữa khi đang chạy thì chỉ đưa cửa sổ chính lên. Nếu bạn chuyển file exe sang thư mục khác, lần chạy kế tiếp Capri tự sửa đường dẫn khởi động.
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
4. Vào **Cài đặt**: dán khóa API cho từng nhà cung cấp muốn dùng, bấm **Tải danh sách mô hình** để chọn mô hình (tên mô hình đổi thường xuyên
   nên luôn lấy từ API), rồi **Lưu vào máy tính**. Khóa được Host mã hóa bằng DPAPI, **không bao giờ gửi ngược lại đầy đủ** và không lưu trên điện thoại.

Khóa lấy ở: Google AI Studio (Gemini), console.groq.com (Groq, `gsk_…`), literouter.com (LiteRouter / DeepSeek V4.1 Flash `:free`),
openrouter.ai (OpenRouter, `sk-or-…`), build.nvidia.com (NVIDIA, `nvapi-…`). **Cloudflare Worker** không cần khóa.

### Nhiều AI, tự chuyển khi hết lượt

Gemini miễn phí chỉ ~20 lượt/ngày nên Capri có **chuỗi nhà cung cấp**: mục **Cài đặt → Thứ tự dùng AI** cho bạn tự xếp thứ tự bằng nút mũi tên
và bật/tắt từng nhà. Capri thử từ trên xuống; nhà nào trả 429 (hết hạn mức) hoặc lỗi thì **nghỉ** một lúc (60 giây cho giới hạn theo phút,
30 phút cho hạn mức theo ngày, 10 phút nếu sai khóa) rồi tự dùng lại, trong lúc đó lệnh chuyển sang nhà kế tiếp. Dòng nhỏ dưới câu trả lời cho biết
"Gemini lỗi/hết lượt, đã chuyển sang Groq" và thời gian từng bước. Cloudflare Worker chỉ hiểu lệnh điều khiển máy; câu nó không hiểu sẽ rơi xuống nhà kế tiếp.
**Tiết kiệm lượt AI** (bật sẵn): công cụ chạy xong là trả lời luôn, không gọi AI lần hai. Đã chạy công cụ rồi mà AI lượt hai lỗi thì Capri
vẫn báo "Đã làm xong: …" (trước đây điện thoại im lặng dù tab đã mở).

## 4. Công cụ Capri điều khiển được

Mở website · **Bật nhạc/video YouTube** (tìm và mở kết quả đầu tiên) · Mở/đóng ứng dụng · Liệt kê cửa sổ · **Phóng to/thu nhỏ/khôi phục/toàn màn hình (F11)** (`window_control`, cửa sổ đang ở trước hoặc theo tên) ·
Dọn dẹp (`%TEMP%` + thùng rác, luôn xin xác nhận) · Bấm phím tắt · Gõ chữ · Chuột (di/bấm/cuộn) · Chụp màn hình (gửi về điện thoại) ·
Nhìn màn hình và trả lời (Gemini) · Nhạc/âm lượng · Khoá máy. Nút nhanh trên điện thoại chạy thẳng công cụ, không tốn lượt AI.

**Trình duyệt:** mặc định Chrome (tìm qua đăng ký Windows rồi `Program Files`); đổi sang Firefox / mặc định Windows / đường dẫn riêng trong Cài đặt → Trình duyệt.
Không tìm thấy thì ghi cảnh báo vào nhật ký + báo trên điện thoại, **không bao giờ âm thầm mở Internet Explorer**. Nút **Đặt Chrome làm trình duyệt mặc định**
thử đặt (Windows 7 cho phép) và **đọc lại để kiểm tra**; không đổi được thì nói thật và mở trang cài đặt để bạn bấm.

Lưu ý YouTube: Host tải trang kết quả và lấy video đầu tiên (cách "tốt nhất có thể"; nếu YouTube đổi trang thì mở trang kết quả tìm kiếm).
Trình duyệt có thể chặn tự phát — khi đó nói "tạm dừng/phát nhạc" hoặc bấm phát một lần.

## 5. Cấu trúc thư mục

```
host/                    Capri Host (net461, C# 5)
  Core/       Settings (DPAPI), Json, Log (vòng 300 dòng), Timeouts (MỌI timeout ở một chỗ), CommandContext (huỷ/hết giờ), Autostart, Native
  Network/    SecureChannel, CapriServer, Pairing, Discovery (UDP), Hub (điều phối, Dừng, log.*)
  Ai/         Agent (chuỗi nhà cung cấp, nghỉ khi 429), Gemini/Cloudflare/Groq/LiteRouter/OpenRouter/NvidiaProvider, OpenAiStyleProvider, Persona, Http (watchdog Abort)
  Actions/    Tools (RunTimed), BrowserLauncher, DefaultBrowser, WindowControl, AppLauncher, Cleaner, InputControl, ScreenTools, YouTube
  Speech/     Greeting (chữ), Mic (ghi âm → Gemini chuyển chữ)
  Ui/         MainForm (cửa sổ chính), PetForm (sprite + bong bóng), Bubble, ChatBox, PairForm, TrayApp
  Assets/     sprites (đã thu nhỏ), font, icon
android/                 Capri Remote (Kotlin, Compose)
  net/        Crypto, SecureSocket, CapriClient (heartbeat + reconnect), Discovery, Timeouts
  data/       Prefs, Chats (lịch sử), LogStore (nhật ký)
  audio/      Recorder
  ui/         Theme, AppViewModel, ChatScreen, LogView (chat Nhật ký lỗi), ConnectScreen, SettingsScreen, Components
.github/workflows/build.yml
```

## 6. Giao thức (tóm tắt)

TCP cổng **47821**; UDP dò tìm cổng **47822**. Khung: `[4 byte độ dài][payload]`. Bắt tay `hello`/`hello_ok` dạng JSON thường (có nonce),
sau đó mọi gói là `iv(16) | AES-256-CBC | HMAC-SHA256(32)` (HMAC phủ hướng + số thứ tự + iv + ciphertext).
Khóa phiên = HMAC(secret, "capri1"|nonce_c|nonce_s); `secret` là token đã ghép đôi hoặc PBKDF2 (20 000 vòng) của mã ghép đôi.
Ping 10 giây/lần; client tự kết nối lại (1 → 15 giây). Sai khóa 5 lần thì Host chặn IP đó 5 phút.
Gói: `chat`, `voice`, `quick`, `settings.get/set`, `models.list`, `confirm.reply` ↔ `reply`, `transcript`, `confirm`, `settings`, `models`.

**Giao thức v2 (bản 1.1.0, chỉ thêm, bản cũ vẫn nối được):** `hello_ok`/`ready` có `v:2`. Mọi gói có `id` **luôn** nhận đúng một `reply`
(lỗi thì `error:true`; bị dừng thì `cancelled:true`; hết giờ thì `timeout:true`; kèm `summary` "AI 3.2s, công cụ 0.4s, tổng 3.9s", `provider`, `switched`).
Mới: `cancel {id}` / `cancel.all` → `cancel.ok`; `log.get {n}` → `log {items, version, v, models, order...}`; `log.push {item}` (đẩy từng dòng mới);
`log.clear`; `confirm.expire {cid}`; `confirm` có thêm `cmd`; `status` có `providers[]`.
Quy tắc thay thế: chat/voice mới từ cùng một điện thoại huỷ chat/voice cũ còn đang chạy; nút nhanh không bao giờ bị huỷ và không huỷ ai.

### Timeout (tất cả ở `host/Core/Timeouts.cs` và `android/.../net/Timeouts.kt`)

HTTP 30 s (có watchdog `Abort()` vì DNS/dò proxy không nằm trong `Timeout` của HttpWebRequest) · một lệnh 85 s · công cụ 15 s (dọn dẹp 60 s, nhìn màn hình 40 s) ·
xác nhận 60 s (hết giờ = "Không", không tính vào hạn lệnh) · bắt tay TCP 10 s · im lặng 45 s = mất kết nối · điện thoại: nối 5 s, chờ trả lời 90 s (giọng nói 120 s).
Mọi timeout đều ghi vào nhật ký và hiện đỏ trong chat.

### Nhật ký lỗi

Trong app: menu ☰ → **Nhật ký lỗi** (chấm đỏ khi có lỗi mới). Lọc Lỗi/Cảnh báo/Thông tin, **Sao chép tất cả** (kèm phiên bản app + Host, giờ, thứ tự và mô hình AI),
**Chia sẻ**, **Xoá**. Tin nhắn lỗi có nút **Xem log** (lọc đúng lệnh đó), **Gửi lại**, **Sửa rồi gửi lại**. Host cũng ghi `%APPDATA%\CapriHost\log.txt` (tự cắt ở 512 KB).
