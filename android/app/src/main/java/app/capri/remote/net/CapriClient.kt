package app.capri.remote.net

import android.os.Build
import android.util.Base64
import app.capri.remote.data.Prefs
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import org.json.JSONObject
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket
import java.net.SocketTimeoutException

sealed interface ConnState {
    data object Idle : ConnState
    data object Connecting : ConnState
    data class Online(val host: String) : ConnState
    data object Offline : ConnState
    data object NeedsPairing : ConnState
    data class Failed(val message: String) : ConnState
}

/**
 * Kết nối TCP tới Capri Host: bắt tay + mã hóa, ping mỗi 10 giây, tự kết nối lại (chờ 1s, 2s, 4s... tối đa 15s).
 * Điện thoại chỉ là bộ điều khiển: mọi xử lý AI nằm trên Host.
 */
class CapriClient(
    private val prefs: Prefs,
    private val scope: CoroutineScope,
    /** Ghi nhật ký phía điện thoại: (mức, nguồn, nội dung). Không bao giờ nuốt lỗi kết nối âm thầm. */
    private val onLog: (String, String, String) -> Unit = { _, _, _ -> }
) {
    private val _state = MutableStateFlow<ConnState>(ConnState.Idle)
    val state: StateFlow<ConnState> = _state

    private val _events = MutableSharedFlow<JSONObject>(extraBufferCapacity = 128)
    val events: SharedFlow<JSONObject> = _events

    @Volatile private var job: Job? = null
    @Volatile private var socket: SecureSocket? = null
    @Volatile private var pairingCode: String? = null
    @Volatile private var wanted = false

    /** Kết nối bằng token đã ghép đôi (nếu có), hoặc bằng mã ghép đôi nếu [code] khác null. */
    fun start(code: String? = null) {
        stop()
        pairingCode = code
        wanted = true
        job = scope.launch(Dispatchers.IO) { loop() }
    }

    fun stop() {
        wanted = false
        job?.cancel()
        job = null
        socket?.close()
        socket = null
        if (_state.value !is ConnState.NeedsPairing && _state.value !is ConnState.Failed) _state.value = ConnState.Idle
    }

    fun forgetPairing() {
        stop()
        prefs.token = null
        _state.value = ConnState.NeedsPairing
    }

    /** Gửi một gói tin. Trả về false nếu đang offline. */
    fun send(message: JSONObject): Boolean {
        val s = socket ?: return false
        scope.launch(Dispatchers.IO) {
            try { s.send(message.toString()) } catch (e: IOException) {
                onLog("WARN", "Mạng", "Gửi gói '${message.optString("t")}' thất bại: ${e.message}")
                s.close()
            }
        }
        return true
    }

    @Volatile private var lastLogged = ""

    private fun logOnce(level: String, text: String) {
        if (text == lastLogged) return
        lastLogged = text
        onLog(level, "Mạng", text)
    }

    private suspend fun loop() {
        var backoff = 1000L
        var tokenFailures = 0
        while (scope.isActive && wanted) {
            _state.value = ConnState.Connecting
            try {
                runSession()
                backoff = 1000L
                tokenFailures = 0
            } catch (e: NeedsPairingException) {
                prefs.token = null
                _state.value = ConnState.NeedsPairing
                wanted = false
                return
            } catch (e: HostMessage) {
                logOnce("ERROR", "Host từ chối kết nối: ${e.message}")
                _state.value = ConnState.Failed(e.message ?: "Host từ chối kết nối")
                wanted = false
                return
            } catch (e: HandshakeFailed) {
                if (pairingCode != null) {
                    _state.value = ConnState.Failed("Sai mã ghép đôi, hoặc Host đã tắt chế độ ghép đôi. Mở \"Ghép đôi điện thoại mới\" trên máy tính rồi thử lại.")
                    wanted = false
                    return
                }
                tokenFailures++
                if (tokenFailures >= 3) {
                    prefs.token = null
                    _state.value = ConnState.NeedsPairing
                    wanted = false
                    return
                }
            } catch (e: IOException) {
                logOnce("WARN", "Mất kết nối tới Host: ${e.javaClass.simpleName} ${e.message ?: ""}".trim())
            } catch (e: kotlinx.coroutines.CancellationException) {
                throw e
            } catch (e: Exception) {
                logOnce("ERROR", "Lỗi bất ngờ khi kết nối: ${e.javaClass.simpleName} ${e.message ?: ""}".trim() + "\n" + android.util.Log.getStackTraceString(e).take(1200))
            }
            socket = null
            if (!wanted) break
            _state.value = ConnState.Offline
            delay(backoff)
            backoff = minOf(backoff * 2, Timeouts.RECONNECT_MAX_MS)
        }
    }

    private class NeedsPairingException : Exception()
    private class HandshakeFailed : Exception()
    private class HostMessage(message: String) : Exception(message)

    private suspend fun runSession() {
        val host = prefs.host
        if (host.isBlank()) throw HostMessage("Chưa nhập địa chỉ Host")
        val raw = Socket()
        var ready = false
        var secure: SecureSocket? = null
        var heartbeat: Job? = null
        try {
            raw.connect(InetSocketAddress(host, prefs.port), Timeouts.CONNECT_MS)
            raw.tcpNoDelay = true
            raw.keepAlive = true
            raw.soTimeout = Timeouts.HANDSHAKE_READ_MS
            secure = SecureSocket(raw)

            val code = pairingCode
            val clientNonce = Crypto.random(16)
            secure.sendPlain(JSONObject()
                .put("t", "hello").put("v", 2)
                .put("id", prefs.deviceId)
                .put("name", Build.MODEL ?: "Android")
                .put("cn", Base64.encodeToString(clientNonce, Base64.NO_WRAP))
                .put("mode", if (code != null) "code" else "token")
                .toString())

            val hello = JSONObject(secure.readPlain())
            if (hello.optString("t") == "error") throw HostMessage(hello.optString("msg", "Host từ chối kết nối"))
            if (code == null && !hello.optBoolean("known", false)) throw NeedsPairingException()

            val serverNonce = Base64.decode(hello.getString("sn"), Base64.NO_WRAP)
            val secret = if (code != null) Crypto.deriveFromCode(code) else (prefs.token ?: throw NeedsPairingException())
            secure.arm(secret, clientNonce, serverNonce)
            secure.send(JSONObject().put("t", "auth").toString())

            // Bắt tay xong. Từ đây đọc liên tục: "paired" (nếu ghép đôi), "ready", rồi mọi sự kiện.
            while (scope.isActive && wanted) {
                val message = try {
                    JSONObject(secure.read())
                } catch (e: AuthException) {
                    if (!ready) throw HandshakeFailed()
                    throw e
                } catch (e: SocketTimeoutException) {
                    throw IOException("Mất tín hiệu từ Host")
                } catch (e: IOException) {
                    if (!ready) throw HandshakeFailed()
                    throw e
                }
                when (message.optString("t")) {
                    "pong" -> Unit
                    "paired" -> {
                        prefs.token = Base64.decode(message.getString("token"), Base64.NO_WRAP)
                        pairingCode = null
                    }
                    "ready" -> {
                        ready = true
                        raw.soTimeout = Timeouts.IDLE_READ_MS       // Host trả pong mỗi 10 giây nên 30 giây im lặng là mất kết nối
                        lastLogged = ""
                        onLog("INFO", "Mạng", "Đã kết nối với Host ${message.optString("host", host)} (bản ${message.optString("version", "?")}, giao thức v${message.optInt("v", 1)})")
                        socket = secure
                        _state.value = ConnState.Online(message.optString("host", host))
                        val s = secure
                        heartbeat = scope.launch(Dispatchers.IO) {
                            while (isActive) {
                                delay(Timeouts.HEARTBEAT_MS)
                                try { s.send("{\"t\":\"ping\"}") } catch (e: IOException) { onLog("WARN", "Mạng", "Ping thất bại: ${e.message}"); s.close(); break }
                            }
                        }
                        _events.tryEmit(message)
                    }
                    else -> _events.tryEmit(message)
                }
            }
        } finally {
            heartbeat?.cancel()
            secure?.close()
            try { raw.close() } catch (e: IOException) { onLog("INFO", "Mạng", "Đóng socket: ${e.message}") }
            socket = null
        }
    }
}
