package app.capri.remote.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import app.capri.remote.R
import app.capri.remote.net.ConnState

/** Màn hình kết nối: nhập IP (hoặc quét LAN) và mã ghép đôi hiện trên máy tính. */
@Composable
fun ConnectScreen(vm: AppViewModel, state: UiState) {
    var host by rememberSaveable(state.host) { mutableStateOf(state.host) }
    var port by rememberSaveable(state.port) { mutableStateOf(state.port.toString()) }
    var code by rememberSaveable { mutableStateOf("") }
    val connecting = state.conn is ConnState.Connecting

    Surface(color = MaterialTheme.colorScheme.background, modifier = Modifier.systemBarsPadding()) {
        Column(
            Modifier.verticalScroll(rememberScrollState()).padding(horizontal = 24.dp, vertical = 16.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Image(painterResource(R.drawable.capri_hero), null, Modifier.size(120.dp))
            Spacer(Modifier.height(8.dp))
            Text("Kết nối với Capri", style = MaterialTheme.typography.headlineMedium)
            Spacer(Modifier.height(8.dp))
            Text(
                "Trên máy tính, mở Capri Host (biểu tượng ở khay hệ thống) → \"Ghép đôi điện thoại mới\". " +
                    "Nhập địa chỉ và mã hiện ra ở đó. Điện thoại và máy tính phải cùng WiFi.",
                style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Spacer(Modifier.height(20.dp))

            OutlinedButton(onClick = { vm.scan() }, enabled = !state.scanning, modifier = Modifier.fillMaxWidth()) {
                if (state.scanning) {
                    CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp)
                    Spacer(Modifier.width(8.dp))
                    Text("Đang tìm…")
                } else {
                    Text("Tìm Capri trong mạng")
                }
            }
            for (found in state.found) {
                Surface(
                    color = MaterialTheme.colorScheme.surface,
                    shape = MaterialTheme.shapes.medium,
                    modifier = Modifier.fillMaxWidth().padding(top = 8.dp).clickable { host = found.address; port = found.port.toString() }
                ) {
                    Row(Modifier.padding(14.dp), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                        Text(found.name, style = MaterialTheme.typography.titleMedium)
                        Text("${found.address}:${found.port}", color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }
            Spacer(Modifier.height(16.dp))

            OutlinedTextField(
                value = host, onValueChange = { host = it }, singleLine = true, label = { Text("Địa chỉ IP của máy tính") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri), modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(10.dp))
            OutlinedTextField(
                value = port, onValueChange = { port = it.filter { c -> c.isDigit() }.take(5) }, singleLine = true, label = { Text("Cổng") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number), modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(10.dp))
            OutlinedTextField(
                value = code, onValueChange = { code = it.uppercase().take(12) }, singleLine = true,
                label = { Text("Mã ghép đôi (chỉ lần đầu)") }, placeholder = { Text("ABCDE-FGHJK") },
                keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Characters), modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(16.dp))

            val message = when (val conn = state.conn) {
                is ConnState.Failed -> conn.message
                is ConnState.NeedsPairing -> "Máy này chưa được ghép đôi. Nhập mã hiện trên máy tính."
                else -> null
            }
            if (message != null) {
                Text(message, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyMedium)
                Spacer(Modifier.height(12.dp))
            }

            Button(
                onClick = { vm.connect(host, port.toIntOrNull() ?: 47821, code) },
                enabled = !connecting && host.isNotBlank(),
                modifier = Modifier.fillMaxWidth().height(50.dp)
            ) {
                if (connecting) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp, color = MaterialTheme.colorScheme.onPrimary)
                else Text(if (code.isBlank()) "Kết nối" else "Ghép đôi và kết nối")
            }
            if (state.host.isNotBlank()) {
                TextButton(onClick = { vm.go(Screen.Chat) }) { Text("Quay lại") }
            }
        }
    }
}
