package app.capri.remote.net

import java.security.SecureRandom
import javax.crypto.Mac
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.PBEKeySpec
import javax.crypto.spec.SecretKeySpec

/**
 * Mật mã dùng chung với Capri Host (C#). Hai bên phải cho ra cùng kết quả từng byte:
 *  - Khóa từ mã ghép đôi: PBKDF2-HMAC-SHA1, salt "capri-pair", 20000 vòng, 32 byte.
 *  - Khóa phiên: HMAC-SHA256(secret, "capri1" | clientNonce | serverNonce), rồi tách enc/mac bằng HMAC.
 */
object Crypto {
    private const val ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"
    private val rng = SecureRandom()

    fun random(size: Int): ByteArray = ByteArray(size).also { rng.nextBytes(it) }

    fun hmac(key: ByteArray, vararg parts: ByteArray): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key, "HmacSHA256"))
        for (part in parts) mac.update(part)
        return mac.doFinal()
    }

    fun normalizeCode(text: String): String = text.uppercase().filter { it in ALPHABET }

    fun deriveFromCode(code: String): ByteArray {
        val spec = PBEKeySpec(normalizeCode(code).toCharArray(), "capri-pair".toByteArray(Charsets.UTF_8), 20000, 256)
        return SecretKeyFactory.getInstance("PBKDF2WithHmacSHA1").generateSecret(spec).encoded
    }

    fun constantTimeEquals(a: ByteArray, b: ByteArray): Boolean {
        if (a.size != b.size) return false
        var diff = 0
        for (i in a.indices) diff = diff or (a[i].toInt() xor b[i].toInt())
        return diff == 0
    }
}
