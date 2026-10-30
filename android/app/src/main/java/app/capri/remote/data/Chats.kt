package app.capri.remote.data

import org.json.JSONArray
import org.json.JSONObject
import java.io.File

data class Note(val tool: String, val ok: Boolean, val text: String)

data class ChatMessage(
    val id: String,
    val role: String,                    // "user" | "assistant"
    val text: String,
    val error: Boolean = false,
    val pending: Boolean = false,
    val notes: List<Note> = emptyList(),
    val image: String? = null,           // đường dẫn ảnh chụp màn hình
    val voice: Boolean = false,          // tin nhắn của người dùng là giọng nói
    val time: Long = System.currentTimeMillis()
)

data class Conversation(
    val id: String,
    val title: String,
    val updated: Long,
    val messages: List<ChatMessage>
)

/** Lịch sử chat lưu thành từng file JSON trong bộ nhớ riêng của app. */
class ChatStore(private val dir: File) {
    init { dir.mkdirs() }

    fun loadAll(): List<Conversation> {
        val files = dir.listFiles { f -> f.extension == "json" } ?: return emptyList()
        return files.mapNotNull { read(it) }.sortedByDescending { it.updated }
    }

    fun save(conversation: Conversation) {
        try {
            File(dir, conversation.id + ".json").writeText(toJson(conversation).toString(), Charsets.UTF_8)
        } catch (_: Exception) { }
    }

    fun delete(id: String) {
        File(dir, "$id.json").delete()
    }

    private fun read(file: File): Conversation? = try {
        val o = JSONObject(file.readText(Charsets.UTF_8))
        val list = ArrayList<ChatMessage>()
        val arr = o.getJSONArray("messages")
        for (i in 0 until arr.length()) {
            val m = arr.getJSONObject(i)
            val notes = ArrayList<Note>()
            val na = m.optJSONArray("notes")
            if (na != null) for (j in 0 until na.length()) {
                val n = na.getJSONObject(j)
                notes.add(Note(n.optString("tool"), n.optBoolean("ok"), n.optString("text")))
            }
            val wasPending = m.optBoolean("pending")
            list.add(
                ChatMessage(
                    id = m.getString("id"),
                    role = m.getString("role"),
                    text = if (wasPending) "Bị gián đoạn." else m.optString("text"),
                    error = m.optBoolean("error") || wasPending,
                    pending = false,
                    notes = notes,
                    image = m.optString("image").ifEmpty { null },
                    voice = m.optBoolean("voice"),
                    time = m.optLong("time")
                )
            )
        }
        Conversation(o.getString("id"), o.optString("title", "Cuộc trò chuyện mới"), o.optLong("updated"), list)
    } catch (_: Exception) {
        null
    }

    private fun toJson(c: Conversation): JSONObject {
        val arr = JSONArray()
        for (m in c.messages) {
            val notes = JSONArray()
            for (n in m.notes) notes.put(JSONObject().put("tool", n.tool).put("ok", n.ok).put("text", n.text))
            arr.put(
                JSONObject()
                    .put("id", m.id).put("role", m.role).put("text", m.text)
                    .put("error", m.error).put("pending", m.pending).put("notes", notes)
                    .put("image", m.image ?: "")
                    .put("voice", m.voice).put("time", m.time)
            )
        }
        return JSONObject().put("id", c.id).put("title", c.title).put("updated", c.updated).put("messages", arr)
    }
}
