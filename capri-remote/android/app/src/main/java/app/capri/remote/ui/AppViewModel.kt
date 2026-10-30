package app.capri.remote.ui

import android.app.Application
import android.util.Base64
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import app.capri.remote.audio.Recorder
import app.capri.remote.data.ChatMessage
import app.capri.remote.data.ChatStore
import app.capri.remote.data.Conversation
import app.capri.remote.data.Note
import app.capri.remote.data.Prefs
import app.capri.remote.net.CapriClient
import app.capri.remote.net.ConnState
import app.capri.remote.net.Discovery
import app.capri.remote.net.FoundHost
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

enum class Screen { Chat, Connect, Settings }

data class ConfirmRequest(val cid: String, val text: String)

data class UiState(
    val screen: Screen = Screen.Chat,
    val conn: ConnState = ConnState.Idle,
    val conversations: List<Conversation> = emptyList(),
    val currentId: String = "",
    val settings: JSONObject? = null,
    val geminiModels: List<String> = emptyList(),
    val nvidiaModels: List<String> = emptyList(),
    val modelsNote: String? = null,
    val confirm: ConfirmRequest? = null,
    val recording: Boolean = false,
    val theme: String = "system",
    val host: String = "",
    val port: Int = 47821,
    val found: List<FoundHost> = emptyList(),
    val scanning: Boolean = false,
    val toast: String? = null
)

/** Toàn bộ trạng thái và logic của app. Màn hình chỉ vẽ UiState và gọi các hàm ở đây. */
class AppViewModel(app: Application) : AndroidViewModel(app) {
    private val prefs = Prefs(app)
    private val store = ChatStore(File(app.filesDir, "chats"))
    private val imageDir = File(app.filesDir, "img").apply { mkdirs() }
    private val recorder = Recorder()
    private val client = CapriClient(prefs, viewModelScope)

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
                }
            }
        }
        viewModelScope.launch { client.events.collect { handleEvent(it) } }
        if (prefs.host.isNotBlank()) client.start()
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

    private fun fail(messageId: String, text: String) = updateMessage(messageId) { it.copy(text = text, error = true, pending = false) }

    private fun newId() = UUID.randomUUID().toString().replace("-", "").take(12)

    // ---------------------------------------------------------------- gửi lệnh

    fun sendText(raw: String) {
        val text = raw.trim()
        if (text.isEmpty()) return
        val convId = _ui.value.currentId
        val id = newId()
        addMessages(convId, ChatMessage("u_$id", "user", text), ChatMessage(id, "assistant", "", pending = true))
        val sent = client.send(JSONObject().put("t", "chat").put("id", id).put("conv", convId).put("text", text))
        if (!sent) fail(id, "Chưa kết nối với Capri Host. Kiểm tra WiFi và Host trên máy tính rồi thử lại.")
    }

    /** Nút nhanh: chạy thẳng một hành động trên máy tính, không qua AI. */
    fun quick(label: String, tool: String, args: JSONObject = JSONObject()) {
        val convId = _ui.value.currentId
        val id = newId()
        addMessages(convId, ChatMessage("u_$id", "user", label), ChatMessage(id, "assistant", "", pending = true))
        val sent = client.send(JSONObject().put("t", "quick").put("id", id).put("tool", tool).put("args", args))
        if (!sent) fail(id, "Chưa kết nối với Capri Host.")
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
            if (!sent) fail(id, "Chưa kết nối với Capri Host.")
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
            "settings" -> {
                _ui.update { it.copy(settings = o.optJSONObject("values")) }
                if (o.optString("id") == "save") toast("Đã lưu cài đặt trên máy tính")
            }
            "models" -> {
                val items = strings(o.optJSONArray("items"))
                val ok = o.optBoolean("ok")
                _ui.update { st ->
                    val note = if (ok) "Tìm thấy ${items.size} mô hình" else o.optString("msg")
                    if (o.optString("provider") == "nvidia") st.copy(nvidiaModels = items, modelsNote = note)
                    else st.copy(geminiModels = items, modelsNote = note)
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
        viewModelScope.launch {
            val imagePath = if (imageData.isNotEmpty()) withContext(Dispatchers.IO) { save(imageDir, "$id.jpg", imageData) } else null
            if (id == "greet") {
                // Lời chào của Capri (chữ) khi vừa kết nối.
                addMessages(_ui.value.currentId, ChatMessage("g_" + System.currentTimeMillis(), "assistant", text))
            } else {
                updateMessage(id) { it.copy(text = text, error = error, pending = false, notes = notes, image = imagePath) }
            }
        }
    }

    private fun save(dir: File, name: String, base64: String): String? = try {
        val file = File(dir, name)
        file.writeBytes(Base64.decode(base64, Base64.DEFAULT))
        file.absolutePath
    } catch (_: Exception) {
        null
    }

    private fun strings(array: JSONArray?): List<String> {
        val list = ArrayList<String>()
        if (array != null) for (i in 0 until array.length()) list.add(array.optString(i))
        return list
    }
}
