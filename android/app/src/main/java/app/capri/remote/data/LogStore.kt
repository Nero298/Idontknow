package app.capri.remote.data

import org.json.JSONObject

/** Một dòng nhật ký: từ Host (fromHost = true) hoặc do chính app điện thoại ghi (mất kết nối, hết giờ chờ...). */
data class LogItem(
    val ts: Long,
    val level: String,          // INFO | WARN | ERROR
    val src: String,
    val msg: String,
    val detail: String = "",
    val cmd: String = "",       // id lệnh = id tin nhắn trong chat
    val fromHost: Boolean = true
) {
    fun key(): String = "$ts|$level|$src|$msg"

    companion object {
        fun fromJson(o: JSONObject): LogItem = LogItem(
            ts = o.optLong("ts"), level = o.optString("level", "INFO"), src = o.optString("src"),
            msg = o.optString("msg"), detail = o.optString("detail"), cmd = o.optString("cmd")
        )
    }
}

object LogFormat {
    private val fmt = java.text.SimpleDateFormat("HH:mm:ss", java.util.Locale.US)

    fun time(ts: Long): String = synchronized(fmt) { fmt.format(java.util.Date(ts)) }

    fun line(i: LogItem, withDetail: Boolean): String {
        val who = if (i.fromHost) "" else "[đt] "
        val cmd = if (i.cmd.isNotEmpty()) " [${i.cmd}]" else ""
        val base = "${time(i.ts)} ${i.level.padEnd(5)} $who${i.src}$cmd: ${i.msg}"
        return if (withDetail && i.detail.isNotEmpty()) base + "\n    " + i.detail.replace("\n", "\n    ") else base
    }

    /** Văn bản để Sao chép / Chia sẻ: phần đầu có phiên bản, giờ và mô hình AI để người khác đọc là hiểu ngay. */
    fun report(appVersion: String, host: JSONObject?, items: List<LogItem>): String {
        val sb = StringBuilder()
        sb.append("Capri Remote Android ").append(appVersion)
        if (host != null) {
            sb.append(" | Host ").append(host.optString("version", "?")).append(" (giao thức v").append(host.optInt("v", 1)).append(")")
            sb.append(" | ").append(host.optString("os"))
        } else sb.append(" | Host: chưa lấy được thông tin")
        sb.append("\nGiờ: ").append(java.text.SimpleDateFormat("yyyy-MM-dd HH:mm:ss", java.util.Locale.US).format(java.util.Date()))
        if (host != null) {
            sb.append("\nThứ tự AI: ").append(host.optString("order"))
            val off = host.optString("off")
            if (off.isNotEmpty()) sb.append(" (tắt: ").append(off).append(")")
            val models = host.optJSONObject("models")
            if (models != null) {
                sb.append("\nMô hình AI: ")
                val keys = models.keys()
                val parts = ArrayList<String>()
                while (keys.hasNext()) { val k = keys.next(); parts.add(k + "=" + models.optString(k)) }
                sb.append(parts.joinToString(", "))
            }
            sb.append("\nTrình duyệt: ").append(host.optString("browser"))
        }
        sb.append("\n").append("-".repeat(40)).append("\n")
        for (i in items) sb.append(line(i, true)).append("\n")
        return sb.toString()
    }
}
