package app.capri.remote.net

import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.IOException
import java.net.Socket
import javax.crypto.Cipher
import javax.crypto.spec.IvParameterSpec
import javax.crypto.spec.SecretKeySpec

class AuthException(message: String) : IOException(message)

/**
 * Khung TCP giống hệt Host: [4 byte độ dài][payload].
 * Bắt tay là JSON thường; sau đó mọi gói là iv(16) | AES-256-CBC | HMAC-SHA256(32),
 * HMAC phủ hướng + số thứ tự + iv + ciphertext (chống sửa gói và phát lại).
 */
class SecureSocket(private val socket: Socket) {
    private val input = DataInputStream(BufferedInputStream(socket.getInputStream()))
    private val output = DataOutputStream(BufferedOutputStream(socket.getOutputStream()))
    private val sendLock = Any()
    private var encKey = ByteArray(0)
    private var macKey = ByteArray(0)
    private var sendSeq = 0L
    private var recvSeq = 0L

    // Điện thoại gửi hướng 0 (client→server), nhận hướng 1 (server→client).
    private val sendDir: Byte = 0
    private val recvDir: Byte = 1

    fun sendPlain(json: String) = writeFrame(json.toByteArray(Charsets.UTF_8))

    fun readPlain(): String = String(readFrame(), Charsets.UTF_8)

    fun arm(secret: ByteArray, clientNonce: ByteArray, serverNonce: ByteArray) {
        val key = Crypto.hmac(secret, "capri1".toByteArray(Charsets.US_ASCII), clientNonce, serverNonce)
        encKey = Crypto.hmac(key, "enc".toByteArray(Charsets.US_ASCII))
        macKey = Crypto.hmac(key, "mac".toByteArray(Charsets.US_ASCII))
    }

    fun send(json: String) {
        val plain = json.toByteArray(Charsets.UTF_8)
        synchronized(sendLock) {
            val iv = Crypto.random(16)
            val cipher = Cipher.getInstance("AES/CBC/PKCS5Padding")
            cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(encKey, "AES"), IvParameterSpec(iv))
            val encrypted = cipher.doFinal(plain)
            val mac = mac(sendDir, sendSeq, iv, encrypted)
            sendSeq++
            writeFrame(iv + encrypted + mac)
        }
    }

    fun read(): String {
        val frame = readFrame()
        if (frame.size < 48) throw AuthException("Gói tin quá ngắn")
        val iv = frame.copyOfRange(0, 16)
        val encrypted = frame.copyOfRange(16, frame.size - 32)
        val mac = frame.copyOfRange(frame.size - 32, frame.size)
        if (!Crypto.constantTimeEquals(mac, mac(recvDir, recvSeq, iv, encrypted))) throw AuthException("Sai khóa hoặc gói bị sửa")
        recvSeq++
        val cipher = Cipher.getInstance("AES/CBC/PKCS5Padding")
        cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(encKey, "AES"), IvParameterSpec(iv))
        return String(cipher.doFinal(encrypted), Charsets.UTF_8)
    }

    fun close() {
        try { socket.close() } catch (_: IOException) { }
    }

    private fun writeFrame(payload: ByteArray) {
        synchronized(output) {
            output.writeInt(payload.size)
            output.write(payload)
            output.flush()
        }
    }

    private fun readFrame(): ByteArray {
        val length = input.readInt()
        if (length < 0 || length > 32 * 1024 * 1024) throw IOException("Gói tin không hợp lệ")
        val data = ByteArray(length)
        input.readFully(data)
        return data
    }

    private fun mac(dir: Byte, seq: Long, iv: ByteArray, encrypted: ByteArray): ByteArray {
        val head = ByteArray(9)
        head[0] = dir
        for (i in 0 until 8) head[1 + i] = (seq shr (56 - 8 * i)).toByte()
        return Crypto.hmac(macKey, head, iv, encrypted)
    }
}
