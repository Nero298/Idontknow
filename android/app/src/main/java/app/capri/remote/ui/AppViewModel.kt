package app.capri.remote.ui

import android.app.Application
import android.util.Base64
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import app.capri.remote.audio.Recorder
import app.capri.remote.data.ChatMessage
import app.capri.remote.data.ChatStore
import app.capri.remote.data.Conversation
import app.capri.remote.data.LogFormat
import app.capri.remote.data.LogItem
import app.capri.remote.data.Note
import app.capri.remote.data.Prefs
import app.capri.remote.net.CapriClient
import app.capri.remote.net.ConnState
import app.capri.remote.net.Discovery
import app.capri.remote.net.FoundHost
import app.capri.remote.net.Timeouts
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.UUID

enum class Screen { Chat, Connect, Settings, Log }

data class ConfirmRequest(val cid: String, val text: String)

data class UiState(
    val screen: Screen = Screen.Chat,
    val conn: ConnState = ConnState.Idle,
    val conversations: List<Conversation> = emptyList(),
    val currentId: String = "",
    val settings: JSONObject? = null,
    val models: Map<String, List<String>> = emptyMap(),     // theo nhà cung cấp: gemini, groq, openrouter, literouter, nvidia
    val modelsNote: String? = null,
    val confirm: ConfirmRequest? = null,
    val recording: Boolean = false,
    val theme: String = "system",
    val host: String = "",
    val port: Int = 47821,
    val found: List<FoundHost> = emptyList(),
    val scanning: Boolean = false,
    val toast: String? = null,
    val logs: List<LogItem> = emptyList(),                  // chat "Nhật ký lỗi" (Host + điện thoại, cũ -> mới)
    val hostInfo: JSONObject? = null,                       // phiên bản/giờ/mô hình AI của Host (cho phần đầu khi Sao chép)
    val logUnread: Boolean = false,                         // chấm đỏ: có lỗi mới chưa xem
    val logCmd: String? = null,                             // đang lọc log theo một lệnh ("Xem log" ở tin nhắn lỗi)
    val draft: String? = null                               // "Sửa rồi gửi lại": nội dung đưa vào ô nhập
)

/** Toàn bộ trạng thái và logic của app. Màn hình chỉ vẽ UiState và gọi các hàm ở đây. */
class AppViewModel(app: Application) : AndroidViewModel(app) {
    private val prefs = Prefs(app)
    private val store = ChatStore(File(app.filesDir, "chats"))
    private val imageDir = File(app.filesDir, "img").apply { mkdirs() }
    private val recorder = Recorder()
    private val client = CapriClient(prefs, viewModelScope) { level, src, msg -> addLocalLog(level, src, msg, "") }
    private val pendingJobs = java.util.concurrent.ConcurrentHashMap<String, Job>()
    private val maxLogs = 400

    private val _ui = MutableStateFlow(UiState())
    val ui: StateFlow<UiState> = _ui

    init {
        var conversations = store.loadAll()
        if (conversations.isEmpty()) conversations = listOf(blankConversation())
        var current = prefs.currentConversation
        if (conversations.none { it.id == current }) current = conversations.first().id
        _ui.value = UiState(
            conversations = conversations, currentId = current,
            theme = prefs.theme, host = prefs.host, port = prefs.port,
            screen = if (prefs.host.isBlank()) Screen.Connect else Screen.Chat
        )

        viewModelScope.launch {
            client.state.collect { state ->
                _ui.update { st ->
                    var screen = st.screen
                    if (state is ConnState.NeedsPairing || state is ConnState.Failed) screen = Screen.Connect
                    if (state is ConnState.Online && st.screen == Screen.Connect) screen = Screen.Chat
                    st.copy(conn = state, screen = screen)
                }
                if (state is ConnState.Online) {
                    client.send(JSONObject().put("t", "settings.get"))
                    client.send(JSONObject().put("t", "log.get").put("n", 100))
                }
                // Mất kết nối khi đang chờ: Host đã huỷ các lệnh đó, đừng để vòng tròn quay mãi.
                if (state is ConnState.Offline || state is ConnState.Failed) {
                    failAllPending("Mất kết nối với máy tính khi đang chờ trả lời. Kết nối lại rồi bấm Gửi lại.")
                }
            }
        }
        viewModelScope.launch {
            client.events.collect { event ->
                // Một gói lạ/hỏng không được phép làm sập luồng nhận: ghi log rồi đọc tiếp.
                try { handleEvent(event) } catch (e: Exception) {
                    addLocalLog("ERROR", "Ứng dụng", "Lỗi xử lý gói '${event.optString("t")}': ${e.javaClass.simpleName} ${e.message}", event.optString("id"))
                }
            }
        }
        if (prefs.host.isNotBlank()) client.start()
    }

    // ---------------------------------------------------------------- nhật ký (chat "Nhật ký lỗi")

    fun appVersion(): String = try {
        val app = getApplication<Application>()
        app.packageManager.getPackageInfo(app.packageName, 0).versionName ?: "?"
    } catch (e: android.content.pm.PackageManager.NameNotFoundException) { "?" }

    private fun addLocalLog(level: String, src: String, msg: String, cmd: String) {
        val item = LogItem(System.currentTimeMillis(), level, src, msg.take(300), if (msg.length > 300) msg.take(1500) else "", cmd, fromHost = false)
        appendLog(item)
    }

    private fun appendLog(item: LogItem) {
        _ui.update { st ->
            if (st.logs.any { it.key() == item.key() }) st
            else st.copy(
                logs = (st.logs + item).takeLast(maxLogs),
                logUnread = st.logUnread || (item.level == "ERROR" && st.screen != Screen.Log)
            )
        }
    }

    /** Mở chat Nhật ký lỗi (tuỳ chọn lọc theo id lệnh) và hỏi Host log mới nhất. */
    fun openLog(cmd: String? = null) {
        _ui.update { it.copy(screen = Screen.Log, logCmd = cmd, logUnread = false) }
        client.send(JSONObject().put("t", "log.get").put("n", 300))
    }

    fun setLogCmd(cmd: String?) = _ui.update { it.copy(logCmd = cmd) }

    fun refreshLog() { client.send(JSONObject().put("t", "log.get").put("n", 300)) }

    fun clearLog() {
        client.send(JSONObject().put("t", "log.clear"))
        _ui.update { it.copy(logs = emptyList(), logUnread = false) }
    }

    fun logReport(items: List<LogItem>): String = LogFormat.report(appVersion(), _ui.value.hostInfo, items)

    private fun mergeHostLog(o: JSONObject) {
        val arr = o.optJSONArray("items")
        val host = ArrayList<LogItem>()
        if (arr != null) for (i in 0 until arr.length()) {
            val item = arr.optJSONObject(i) ?: continue
            host.add(LogItem.fromJson(item))
        }
        _ui.update { st ->
            val local = st.logs.filter { !it.fromHost }
            st.copy(logs = (host + local).sortedBy { it.ts }.takeLast(maxLogs), hostInfo = o)
        }
    }

    override fun onCleared() {
        client.stop()
        recorder.cancel()
    }

    // ---------------------------------------------------------------- điều hướng / cài đặt cục bộ

    fun go(screen: Screen) = _ui.update { it.copy(screen = screen) }

    fun setTheme(mode: String) {
        prefs.theme = mode
        _ui.update { it.copy(theme = mode) }
    }

    fun consumeToast() = _ui.update { it.copy(toast = null) }

    fun consumeDraft() = _ui.update { it.copy(draft = null) }

    private fun toast(text: String) = _ui.update { it.copy(toast = text) }

    // ---------------------------------------------------------------- kết nối

    fun connect(host: String, port: Int, code: String) {
        val cleanHost = host.trim()
        if (cleanHost.isEmpty()) { toast("Nhập địa chỉ IP của máy tính"); return }
        prefs.host = cleanHost
        prefs.port = port
        _ui.update { it.copy(host = cleanHost, port = port) }
        client.start(code.takeIf { it.isNotBlank() })
    }

    fun reconnect() {
        if (prefs.host.isNotBlank()) client.start()
    }

    fun forgetHost() {
        client.forgetPairing()
        prefs.host = ""
        _ui.update { it.copy(host = "", settings = null, screen = Screen.Connect) }
    }

    fun scan() {
        viewModelScope.launch {
            _ui.update { it.copy(scanning = true, found = emptyList()) }
            val found = Discovery.scan()
            _ui.update { it.copy(scanning = false, found = found) }
            if (found.isEmpty()) toast("Không thấy Capri Host. Kiểm tra cùng WiFi và tường lửa Windows.")
        }
    }

    // ---------------------------------------------------------------- cuộc trò chuyện

    private fun blankConversation() = Conversation(UUID.randomUUID().toString(), "Cuộc trò chuyện mới", System.currentTimeMillis(), emptyList())

    fun newChat() {
        val st = _ui.value
        val current = st.conversations.firstOrNull { it.id == st.currentId }
        if (current != null && current.messages.isEmpty()) { go(Screen.Chat); return }
        val fresh = blankConversation()
        _ui.update { it.copy(conversations = listOf(fresh) + it.conversations, currentId = fresh.id, screen = Screen.Chat) }
        prefs.currentConversation = fresh.id
    }

    fun selectChat(id: String) {
        prefs.currentConversation = id
        _ui.update { it.copy(currentId = id, screen = Screen.Chat) }
    }

    fun deleteChat(id: String) {
        client.send(JSONObject().put("t", "chat.reset").put("conv", id))
        viewModelScope.launch(Dispatchers.IO) { store.delete(id) }
        _ui.update { st ->
            var rest = st.conversations.filter { it.id != id }
            if (rest.isEmpty()) rest = listOf(blankConversation())
            val current = if (st.currentId == id) rest.first().id else st.currentId
            prefs.currentConversation = current
            st.copy(conversations = rest, currentId = current)
        }
    }

    private fun mutate(convId: String, change: (Conversation) -> Conversation) {
        var saved: Conversation? = null
        _ui.update { st ->
            st.copy(conversations = st.conversations.map { c ->
                if (c.id == convId) change(c).also { saved = it } else c
            })
        }
        saved?.let { c -> viewModelScope.launch(Dispatchers.IO) { store.save(c) } }
    }

    private fun addMessages(convId: String, vararg messages: ChatMessage) {
        mutate(convId) { c ->
            val firstUser = messages.firstOrNull { it.role == "user" }
            val title = if (c.messages.isEmpty() && firstUser != null) firstUser.text.replace("\n", " ").take(40) else c.title
            c.copy(title = title, updated = System.currentTimeMillis(), messages = c.messages + messages)
        }
        _ui.update { st -> st.copy(conversations = st.conversations.sortedByDescending { it.updated }) }
    }

    private fun updateMessage(messageId: String, change: (ChatMessage) -> ChatMessage) {
        val conv = _ui.value.conversations.firstOrNull { c -> c.messages.any { it.id == messageId } } ?: return
        mutate(conv.id) { c -> c.copy(messages = c.messages.map { if (it.id == messageId) change(it) else it }) }
    }

    private fun fail(messageId: String, text: String) {
        pendingJobs.remove(messageId)?.cancel()
        updateMessage(messageId) { it.copy(text = text, error = true, pending = false) }
        addLocalLog("ERROR", "Lệnh", text, messageId)
    }

    private fun isPending(messageId: String): Boolean =
        _ui.value.conversations.any { c -> c.messages.any { it.id == messageId && it.pending } }

    private fun failAllPending(text: String) {
        val ids = _ui.value.conversations.flatMap { c -> c.messages.filter { it.pending }.map { it.id } }
        for (id in ids) fail(id, text)
    }

    /** Hẹn giờ chờ trả lời: hết hạn thì báo lỗi đỏ (không bao giờ quay vô hạn) và nhờ Host dừng lệnh đó. */
    private fun watch(id: String, ms: Long) {
        pendingJobs.remove(id)?.cancel()
        pendingJobs[id] = viewModelScope.launch {
            delay(ms)
            if (isPending(id)) {
                client.send(JSONObject().put("t", "cancel").put("id", id))
                fail(id, "Hết ${ms / 1000} giây mà Host chưa trả lời. Kiểm tra Host đang chạy và mạng WiFi, rồi bấm Gửi lại hoặc Xem log.")
            }
        }
    }

    // ---------------------------------------------------------------- dừng / gửi lại

    /** Bấm Dừng: báo Host huỷ ngay và đánh dấu "Đã dừng" tại chỗ (không đợi Host). */
    fun stop(id: String) {
        pendingJobs.remove(id)?.cancel()
        client.send(JSONObject().put("t", "cancel").put("id", id))
        updateMessage(id) { it.copy(text = "Đã dừng.", cancelled = true, error = false, pending = false) }
    }

    fun stopAll() {
        val ids = _ui.value.conversations.flatMap { c -> c.messages.filter { it.pending }.map { it.id } }
        client.send(JSONObject().put("t", "cancel.all"))
        for (id in ids) {
            pendingJobs.remove(id)?.cancel()
            updateMessage(id) { it.copy(text = "Đã dừng.", cancelled = true, error = false, pending = false) }
        }
    }

    private fun userTextFor(assistantId: String): String? {
        for (c in _ui.value.conversations) {
            val m = c.messages.firstOrNull { it.id == "u_$assistantId" }
            if (m != null) return m.text
        }
        return null
    }

    fun resend(assistantId: String) {
        val text = userTextFor(assistantId)
        if (text.isNullOrBlank() || text.startsWith("Đang nhận dạng")) { toast("Không còn nội dung để gửi lại"); return }
        sendText(text)
    }

    fun editAndResend(assistantId: String) {
        val text = userTextFor(assistantId)
        if (text.isNullOrBlank() || text.startsWith("Đang nhận dạng")) { toast("Không còn nội dung để sửa"); return }
        _ui.update { it.copy(draft = text) }
    }

    private fun newId() = UUID.randomUUID().toString().replace("-", "").take(12)

    // ---------------------------------------------------------------- gửi lệnh

    fun sendText(raw: String) {
        val text = raw.trim()
        if (text.isEmpty()) return
        val convId = _ui.value.currentId
        val id = newId()
        addMessages(convId, ChatMessage("u_$id", "user", text), ChatMessage(id, "assistant", "", pending = true))
        val sent = client.send(JSONObject().put("t", "chat").put("id", id).put("conv", convId).put("text", text))
        if (!sent) fail(id, "Chưa kết nối với Capri Host. Kiểm tra WiFi và Host trên máy tính rồi thử lại.") else watch(id, Timeouts.REPLY_CHAT_MS)
    }

    /** Nút nhanh: chạy thẳng một hành động trên máy tính, không qua AI. */
    fun quick(label: String, tool: String, args: JSONObject = JSONObject()) {
        val convId = _ui.value.currentId
        val id = newId()
        addMessages(convId, ChatMessage("u_$id", "user", label), ChatMessage(id, "assistant", "", pending = true))
        val sent = client.send(JSONObject().put("t", "quick").put("id", id).put("tool", tool).put("args", args))
        if (!sent) fail(id, "Chưa kết nối với Capri Host.") else watch(id, Timeouts.REPLY_QUICK_MS)
    }

    fun startRecording(): Boolean {
        val ok = recorder.start()
        if (ok) _ui.update { it.copy(recording = true) } else toast("Không mở được micro")
        return ok
    }

    fun recordingSeconds(): Int = recorder.seconds

    fun cancelRecording() {
        recorder.cancel()
        _ui.update { it.copy(recording = false) }
    }

    fun stopAndSend() {
        val wav = recorder.stop()
        _ui.update { it.copy(recording = false) }
        if (wav == null) { toast("Ghi âm quá ngắn, thử nói lâu hơn một chút"); return }
        val convId = _ui.value.currentId
        val id = newId()
        addMessages(convId, ChatMessage("u_$id", "user", "Đang nhận dạng giọng nói…", voice = true), ChatMessage(id, "assistant", "", pending = true))
        viewModelScope.launch(Dispatchers.IO) {
            val data = Base64.encodeToString(wav, Base64.NO_WRAP)
            val sent = client.send(JSONObject().put("t", "voice").put("id", id).put("conv", convId).put("data", data))
            if (!sent) fail(id, "Chưa kết nối với Capri Host.") else watch(id, Timeouts.REPLY_VOICE_MS)
        }
    }

    fun answerConfirm(ok: Boolean) {
        val request = _ui.value.confirm ?: return
        client.send(JSONObject().put("t", "confirm.reply").put("cid", request.cid).put("ok", ok))
        _ui.update { it.copy(confirm = null) }
    }

    // ---------------------------------------------------------------- cài đặt trên Host (khóa API, mô hình, giọng nói...)

    fun refreshSettings() {
        client.send(JSONObject().put("t", "settings.get"))
    }

    fun saveSettings(values: JSONObject) {
        if (!client.send(JSONObject().put("t", "settings.set").put("id", "save").put("values", values))) toast("Chưa kết nối với Host")
    }

    fun fetchModels(provider: String, typedKey: String) {
        _ui.update { it.copy(modelsNote = "Đang tải danh sách…") }
        if (!client.send(JSONObject().put("t", "models.list").put("id", "m").put("provider", provider).put("key", typedKey))) {
            _ui.update { it.copy(modelsNote = "Chưa kết nối với Host") }
        }
    }

    // ---------------------------------------------------------------- sự kiện từ Host

    private fun handleEvent(o: JSONObject) {
        when (o.optString("t")) {
            "reply" -> onReply(o)
            "transcript" -> updateMessage("u_" + o.optString("id")) { it.copy(text = o.optString("text")) }
            "confirm" -> _ui.update { it.copy(confirm = ConfirmRequest(o.optString("cid"), o.optString("text"))) }
            "confirm.expire" -> _ui.update { st -> if (st.confirm?.cid == o.optString("cid")) st.copy(confirm = null) else st }
            "log" -> mergeHostLog(o)
            "log.push" -> {
                val item = o.optJSONObject("item")
                if (item != null) appendLog(LogItem.fromJson(item))
            }
            "cancel.ok" -> Unit
            "settings" -> {
                _ui.update { it.copy(settings = o.optJSONObject("values")) }
                if (o.optString("id") == "save") toast("Đã lưu cài đặt trên máy tính")
            }
            "models" -> {
                val items = strings(o.optJSONArray("items"))
                val ok = o.optBoolean("ok")
                _ui.update { st ->
                    val note = if (ok) "Tìm thấy ${items.size} mô hình" else o.optString("msg")
                    st.copy(models = st.models + (o.optString("provider") to items), modelsNote = note)
                }
            }
        }
    }

    private fun onReply(o: JSONObject) {
        val id = o.optString("id")
        val text = o.optString("text")
        val error = o.optBoolean("error")
        val notes = ArrayList<Note>()
        val na = o.optJSONArray("notes")
        if (na != null) for (i in 0 until na.length()) {
            val n = na.getJSONObject(i)
            notes.add(Note(n.optString("tool"), n.optBoolean("ok"), n.optString("text")))
        }
        val imageData = o.optString("image")
        pendingJobs.remove(id)?.cancel()
        val cancelled = o.optBoolean("cancelled")
        val infoParts = ArrayList<String>()
        if (o.optString("switched").isNotEmpty()) infoParts.add("↪ " + o.optString("switched"))
        if (o.optString("summary").isNotEmpty()) infoParts.add("⏱ " + o.optString("summary"))
        val info = infoParts.joinToString("\n")
        if (error && id != "greet") refreshLog()
        viewModelScope.launch {
            val imagePath = if (imageData.isNotEmpty()) withContext(Dispatchers.IO) { save(imageDir, "$id.jpg", imageData) } else null
            if (id == "greet") {
                // Lời chào của Capri (chữ) khi vừa kết nối.
                addMessages(_ui.value.currentId, ChatMessage("g_" + System.currentTimeMillis(), "assistant", text))
            } else {
                updateMessage(id) {
                    if (it.cancelled && !cancelled) it       // người dùng đã bấm Dừng tại chỗ: giữ "Đã dừng"
                    else it.copy(text = text, error = error, pending = false, notes = notes, image = imagePath, cancelled = cancelled, info = info)
                }
            }
        }
    }

    private fun save(dir: File, name: String, base64: String): String? = try {
        val file = File(dir, name)
        file.writeBytes(Base64.decode(base64, Base64.DEFAULT))
        file.absolutePath
    } catch (e: java.io.IOException) {
        addLocalLog("WARN", "Ứng dụng", "Không lưu được ảnh $name: ${e.message}", "")
        null
    } catch (e: IllegalArgumentException) {
        addLocalLog("WARN", "Ứng dụng", "Ảnh $name gửi lên bị hỏng: ${e.message}", "")
        null
    }

    private fun strings(array: JSONArray?): List<String> {
        val list = ArrayList<String>()
        if (array != null) for (i in 0 until array.length()) list.add(array.optString(i))
        return list
    }
}
