package app.capri.remote.data

import android.content.Context
import android.util.Base64
import java.util.UUID

/** Cài đặt cục bộ trên điện thoại (địa chỉ Host, token ghép đôi, giao diện). Khóa API KHÔNG lưu ở đây. */
class Prefs(context: Context) {
    private val sp = context.getSharedPreferences("capri", Context.MODE_PRIVATE)

    var host: String
        get() = sp.getString("host", "") ?: ""
        set(value) { sp.edit().putString("host", value).apply() }

    var port: Int
        get() = sp.getInt("port", 47821)
        set(value) { sp.edit().putInt("port", value).apply() }

    val deviceId: String
        get() {
            var id = sp.getString("device_id", null)
            if (id == null) {
                id = UUID.randomUUID().toString().replace("-", "")
                sp.edit().putString("device_id", id).apply()
            }
            return id
        }

    var token: ByteArray?
        get() = sp.getString("token", null)?.let { Base64.decode(it, Base64.NO_WRAP) }
        set(value) {
            sp.edit().apply {
                if (value == null) remove("token") else putString("token", Base64.encodeToString(value, Base64.NO_WRAP))
            }.apply()
        }

    /** "system" | "light" | "dark" */
    var theme: String
        get() = sp.getString("theme", "system") ?: "system"
        set(value) { sp.edit().putString("theme", value).apply() }

    var currentConversation: String
        get() = sp.getString("current", "") ?: ""
        set(value) { sp.edit().putString("current", value).apply() }
}
