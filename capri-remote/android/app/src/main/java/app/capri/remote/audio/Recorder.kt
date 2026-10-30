package app.capri.remote.audio

import android.annotation.SuppressLint
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder

/** Ghi âm micro 16 kHz mono 16-bit rồi đóng gói WAV để gửi cho Host (Gemini nghe và hiểu). */
class Recorder {
    private val rate = 16000
    private val pcm = ByteArrayOutputStream()
    @Volatile private var running = false
    private var thread: Thread? = null

    val isRecording: Boolean get() = running

    val seconds: Int
        get() = synchronized(pcm) { pcm.size() / (rate * 2) }

    /** Trả về false nếu không mở được micro (chưa cấp quyền hoặc micro đang bị app khác dùng). */
    @SuppressLint("MissingPermission")
    fun start(): Boolean {
        if (running) return true
        val minimum = AudioRecord.getMinBufferSize(rate, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT)
        if (minimum <= 0) return false
        val record = try {
            AudioRecord(MediaRecorder.AudioSource.MIC, rate, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT, maxOf(minimum, 8192))
        } catch (_: Exception) {
            return false
        }
        if (record.state != AudioRecord.STATE_INITIALIZED) {
            record.release()
            return false
        }
        synchronized(pcm) { pcm.reset() }
        running = true
        thread = Thread {
            val chunk = ByteArray(4096)
            try {
                record.startRecording()
                while (running) {
                    val n = record.read(chunk, 0, chunk.size)
                    if (n > 0) synchronized(pcm) { pcm.write(chunk, 0, n) }
                    if (seconds >= 60) running = false      // tối đa 60 giây mỗi lần nói
                }
            } catch (_: Exception) {
            } finally {
                try { record.stop() } catch (_: Exception) { }
                record.release()
            }
        }.also {
            it.name = "capri-recorder"
            it.start()
        }
        return true
    }

    /** Dừng và trả về WAV, hoặc null nếu quá ngắn (dưới ~0,25 giây). */
    fun stop(): ByteArray? {
        running = false
        thread?.join(1500)
        thread = null
        val data = synchronized(pcm) { pcm.toByteArray() }
        return if (data.size < rate / 2) null else wav(data)
    }

    fun cancel() {
        running = false
        thread?.join(1500)
        thread = null
        synchronized(pcm) { pcm.reset() }
    }

    private fun wav(data: ByteArray): ByteArray {
        val header = ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN)
        header.put("RIFF".toByteArray()).putInt(36 + data.size).put("WAVE".toByteArray())
        header.put("fmt ".toByteArray()).putInt(16).putShort(1).putShort(1).putInt(rate).putInt(rate * 2).putShort(2).putShort(16)
        header.put("data".toByteArray()).putInt(data.size)
        return header.array() + data
    }
}
