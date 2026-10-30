package app.capri.remote.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.consumeWindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.outlined.Visibility
import androidx.compose.material.icons.outlined.VisibilityOff
import androidx.compose.material3.Button
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import app.capri.remote.net.ConnState
import org.json.JSONObject

@Composable
private fun fieldColors() = OutlinedTextFieldDefaults.colors(
    focusedBorderColor = MaterialTheme.colorScheme.primary,
    unfocusedBorderColor = MaterialTheme.colorScheme.outlineVariant,
    focusedContainerColor = MaterialTheme.colorScheme.surface,
    unfocusedContainerColor = MaterialTheme.colorScheme.surface
)

@Composable
private fun Section(title: String, content: @Composable () -> Unit) {
    Column(Modifier.fillMaxWidth().padding(top = 22.dp)) {
        Text(title, style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.primary, modifier = Modifier.padding(start = 4.dp, bottom = 8.dp))
        Surface(
            shape = RoundedCornerShape(16.dp), color = MaterialTheme.colorScheme.surface,
            border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant), modifier = Modifier.fillMaxWidth()
        ) {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) { content() }
        }
    }
}

@Composable
private fun SwitchRow(label: String, description: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(label, style = MaterialTheme.typography.titleMedium)
            if (description.isNotEmpty()) Text(description, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Spacer(Modifier.width(12.dp))
        Switch(checked = checked, onCheckedChange = onChange)
    }
}

/** Ô nhập khóa API: ẩn mặc định, hiển thị trạng thái "đã lưu trên Host" và 4 ký tự cuối. Khóa không bao giờ được Host gửi lại đầy đủ. */
@Composable
private fun SecretField(label: String, isSet: Boolean, hint: String, value: String, onValue: (String) -> Unit, onClear: () -> Unit) {
    var visible by remember { mutableStateOf(false) }
    OutlinedTextField(
        value = value, onValueChange = onValue, singleLine = true, label = { Text(label) },
        placeholder = { Text(if (isSet) "Nhập khóa mới để thay" else "Dán khóa API vào đây") },
        supportingText = { Text(if (isSet) "Đã lưu trên máy tính ($hint)" else "Chưa có khóa") },
        visualTransformation = if (visible) VisualTransformation.None else PasswordVisualTransformation(),
        trailingIcon = {
            IconButton(onClick = { visible = !visible }) {
                Icon(if (visible) Icons.Outlined.VisibilityOff else Icons.Outlined.Visibility, "Hiện/ẩn")
            }
        },
        colors = fieldColors(), modifier = Modifier.fillMaxWidth()
    )
    if (isSet) TextButton(onClick = onClear) { Text("Xoá khóa đã lưu") }
}

@Composable
private fun ModelPicker(model: String, onModel: (String) -> Unit, options: List<String>, onFetch: () -> Unit, note: String?) {
    var open by remember { mutableStateOf(false) }
    OutlinedTextField(
        value = model, onValueChange = onModel, singleLine = true, label = { Text("Mô hình") },
        colors = fieldColors(), modifier = Modifier.fillMaxWidth()
    )
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        OutlinedButton(onClick = onFetch) { Text("Tải danh sách mô hình") }
        if (options.isNotEmpty()) {
            Box {
                OutlinedButton(onClick = { open = true }) { Text("Chọn (${options.size})") }
                DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
                    for (name in options.take(80)) DropdownMenuItem(text = { Text(name) }, onClick = { onModel(name); open = false })
                }
            }
        }
    }
    if (note != null) Text(note, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
}

@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
fun SettingsScreen(vm: AppViewModel, state: UiState) {
    Scaffold(
        containerColor = MaterialTheme.colorScheme.background,
        topBar = {
            TopAppBar(
                title = { Text("Cài đặt", style = MaterialTheme.typography.titleLarge) },
                navigationIcon = { IconButton(onClick = { vm.go(Screen.Chat) }) { Icon(Icons.AutoMirrored.Outlined.ArrowBack, "Quay lại") } },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.background)
            )
        }
    ) { padding ->
        Column(
            Modifier.padding(padding).consumeWindowInsets(padding).fillMaxSize()
                .verticalScroll(rememberScrollState()).padding(horizontal = 16.dp).padding(bottom = 32.dp)
        ) {
            Section("Kết nối") {
                val status = when (val c = state.conn) {
                    is ConnState.Online -> "Đang kết nối với ${c.host}"
                    is ConnState.Connecting -> "Đang kết nối…"
                    is ConnState.NeedsPairing -> "Chưa ghép đôi"
                    is ConnState.Failed -> c.message
                    else -> "Ngoại tuyến"
                }
                Text("Máy tính: ${state.host.ifBlank { "chưa chọn" }}:${state.port}", style = MaterialTheme.typography.titleMedium)
                Text(status, color = MaterialTheme.colorScheme.onSurfaceVariant)
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = { vm.reconnect() }) { Text("Kết nối lại") }
                    OutlinedButton(onClick = { vm.forgetHost() }) { Text("Ghép đôi lại") }
                }
            }

            val settings = state.settings
            if (settings == null) {
                Section("Cài đặt trên máy tính") {
                    Text(
                        "Kết nối với Capri Host để chỉnh khóa API, mô hình AI và tính cách. Các thiết lập này được lưu (mã hóa) trên máy tính, không lưu trên điện thoại.",
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            } else {
                HostSettings(vm, state, settings)
            }

            Section("Giao diện") {
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    for ((mode, label) in listOf("system" to "Theo hệ thống", "light" to "Sáng", "dark" to "Tối")) {
                        FilterChip(selected = state.theme == mode, onClick = { vm.setTheme(mode) }, label = { Text(label) })
                    }
                }
            }
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun HostSettings(vm: AppViewModel, state: UiState, s: JSONObject) {
    // Trạng thái chỉnh sửa; khởi tạo lại mỗi khi Host gửi cài đặt mới (đối tượng s đổi).
    var geminiKey by remember(s) { mutableStateOf("") }
    var nvidiaKey by remember(s) { mutableStateOf("") }
    var geminiModel by remember(s) { mutableStateOf(s.optString("gemini_model")) }
    var nvidiaModel by remember(s) { mutableStateOf(s.optString("nvidia_model")) }
    var primary by remember(s) { mutableStateOf(s.optString("primary", "gemini")) }
    var failover by remember(s) { mutableStateOf(s.optBoolean("failover", true)) }
    var language by remember(s) { mutableStateOf(s.optString("language", "Vietnamese")) }
    var nickname by remember(s) { mutableStateOf(s.optString("nickname")) }
    var wordiness by remember(s) { mutableStateOf(s.optInt("wordiness", 0)) }
    var personality by remember(s) { mutableStateOf(s.optString("personality")) }
    var allowInput by remember(s) { mutableStateOf(s.optBoolean("allow_input", true)) }
    var greet by remember(s) { mutableStateOf(s.optBoolean("greet", true)) }
    var customApps by remember(s) { mutableStateOf(s.optString("custom_apps")) }

    Section("Gemini (AI chính)") {
        SecretField(
            "Khóa API Gemini", s.optBoolean("gemini_key_set"), s.optString("gemini_key_hint"), geminiKey, { geminiKey = it },
            onClear = { vm.saveSettings(JSONObject().put("gemini_key", "")) }
        )
        ModelPicker(geminiModel, { geminiModel = it }, state.geminiModels, { vm.fetchModels("gemini", geminiKey) }, state.modelsNote)
    }

    Section("NVIDIA (dự phòng)") {
        SecretField(
            "Khóa API NVIDIA (nvapi-…)", s.optBoolean("nvidia_key_set"), s.optString("nvidia_key_hint"), nvidiaKey, { nvidiaKey = it },
            onClear = { vm.saveSettings(JSONObject().put("nvidia_key", "")) }
        )
        ModelPicker(nvidiaModel, { nvidiaModel = it }, state.nvidiaModels, { vm.fetchModels("nvidia", nvidiaKey) }, null)
        Text("Nhà cung cấp ưu tiên", style = MaterialTheme.typography.titleMedium)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            FilterChip(selected = primary == "gemini", onClick = { primary = "gemini" }, label = { Text("Gemini trước") })
            FilterChip(selected = primary == "nvidia", onClick = { primary = "nvidia" }, label = { Text("NVIDIA trước") })
        }
        SwitchRow("Tự chuyển nhà cung cấp khi lỗi", "Gemini lỗi hoặc hết hạn mức thì thử NVIDIA (và ngược lại).", failover) { failover = it }
    }

    Section("Tính cách Capri") {
        OutlinedTextField(value = language, onValueChange = { language = it }, singleLine = true, label = { Text("Ngôn ngữ trả lời") }, colors = fieldColors(), modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = nickname, onValueChange = { nickname = it }, singleLine = true, label = { Text("Capri gọi bạn là") }, colors = fieldColors(), modifier = Modifier.fillMaxWidth())
        Text("Độ dài câu trả lời", style = MaterialTheme.typography.titleMedium)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            for ((value, label) in listOf(0 to "Ngắn", 1 to "Vừa", 2 to "Dài")) {
                FilterChip(selected = wordiness == value, onClick = { wordiness = value }, label = { Text(label) })
            }
        }
        OutlinedTextField(
            value = personality, onValueChange = { personality = it }, minLines = 3, maxLines = 8,
            label = { Text("Tính cách tự viết (tuỳ chọn)") }, supportingText = { Text("Để trống = tính cách Capri gốc") },
            colors = fieldColors(), modifier = Modifier.fillMaxWidth()
        )
        SwitchRow("Capri chào khi kết nối", "Bong bóng chữ, chưa có giọng nói.", greet) { greet = it }
    }

    Section("Điều khiển máy tính") {
        SwitchRow("Cho phép điều khiển chuột và bàn phím", "Tắt thì Capri chỉ mở/đóng app, mở web, dọn dẹp, chụp màn hình.", allowInput) { allowInput = it }
        OutlinedTextField(
            value = customApps, onValueChange = { customApps = it }, minLines = 2, maxLines = 6,
            label = { Text("Ứng dụng tuỳ chỉnh") }, supportingText = { Text("Mỗi dòng: tên=đường dẫn. Ví dụ: game=D:\\Games\\game.exe") },
            colors = fieldColors(), modifier = Modifier.fillMaxWidth()
        )
    }

    Spacer(Modifier.height(20.dp))
    Button(
        onClick = {
            val values = JSONObject()
                .put("gemini_model", geminiModel).put("nvidia_model", nvidiaModel)
                .put("primary", primary).put("failover", failover)
                .put("language", language).put("nickname", nickname).put("wordiness", wordiness).put("personality", personality)
                .put("greet", greet).put("allow_input", allowInput).put("custom_apps", customApps)
            // Khóa chỉ gửi khi người dùng vừa nhập khóa mới.
            if (geminiKey.isNotBlank()) values.put("gemini_key", geminiKey.trim())
            if (nvidiaKey.isNotBlank()) values.put("nvidia_key", nvidiaKey.trim())
            vm.saveSettings(values)
        },
        modifier = Modifier.fillMaxWidth().height(50.dp)
    ) { Text("Lưu vào máy tính") }
}
