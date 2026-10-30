package app.capri.remote.net

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException

data class FoundHost(val name: String, val address: String, val port: Int)

/** Tìm Capri Host trong LAN bằng UDP broadcast (Host lắng nghe cổng TCP + 1). */
object Discovery {
    suspend fun scan(tcpPort: Int = 47821): List<FoundHost> = withContext(Dispatchers.IO) {
        val found = LinkedHashMap<String, FoundHost>()
        var socket: DatagramSocket? = null
        try {
            socket = DatagramSocket()
            socket.broadcast = true
            socket.soTimeout = 400
            val message = "CAPRI_DISCOVER".toByteArray(Charsets.UTF_8)
            for (target in broadcastTargets()) {
                try {
                    socket.send(DatagramPacket(message, message.size, target, tcpPort + 1))
                } catch (_: Exception) { }
            }
            val buffer = ByteArray(512)
            val deadline = System.currentTimeMillis() + 1800
            while (System.currentTimeMillis() < deadline) {
                try {
                    val packet = DatagramPacket(buffer, buffer.size)
                    socket.receive(packet)
                    val text = String(packet.data, 0, packet.length, Charsets.UTF_8).trim()
                    val parts = text.split("|")
                    if (parts.size >= 3 && parts[0] == "CAPRI_HOST") {
                        val address = packet.address.hostAddress ?: continue
                        found[address] = FoundHost(parts[1], address, parts[2].toIntOrNull() ?: tcpPort)
                    }
                } catch (_: SocketTimeoutException) { }
            }
        } catch (_: Exception) {
        } finally {
            socket?.close()
        }
        found.values.toList()
    }

    private fun broadcastTargets(): List<InetAddress> {
        val list = ArrayList<InetAddress>()
        try {
            for (nic in NetworkInterface.getNetworkInterfaces()) {
                if (!nic.isUp || nic.isLoopback) continue
                for (ia in nic.interfaceAddresses) {
                    val broadcast = ia.broadcast
                    if (broadcast is Inet4Address) list.add(broadcast)
                }
            }
        } catch (_: Exception) { }
        try { list.add(InetAddress.getByName("255.255.255.255")) } catch (_: Exception) { }
        return list
    }
}
