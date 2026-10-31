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
import androidx.compose.material.icons.outlined.ArrowDownward
import androidx.compose.material.icons.outlined.ArrowUpward
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
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateMapOf
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

private class ProviderInfo(val name: String, val label: String, val keyField: String?, val modelField: String?)

private val Providers = listOf(
    ProviderInfo("gemini", "Gemini", "gemini_key", "gemini_model"),
    ProviderInfo("cloudflare", "Cloudflare Worker", null, null),
    ProviderInfo("groq", "Groq", "groq_key", "groq_model"),
    ProviderInfo("literouter", "LiteRouter (DeepSeek)", "literouter_key", "literouter_model"),
    ProviderInfo("openrouter", "OpenRouter", "openrouter_key", "openrouter_model"),
    ProviderInfo("nvidia", "NVIDIA", "nvidia_key", "nvidia_model")
)

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
    // ---- Nhà cung cấp AI: thứ tự ưu tiên do người dùng tự sắp xếp, mỗi nhà có công tắc bật/tắt.
    val order = remember(s) { mutableStateListOf<String>().also { list -> list.addAll(s.optString("provider_order").split(",").map { it.trim() }.filter { it.isNotEmpty() }) } }
    val off = remember(s) { mutableStateListOf<String>().also { list -> list.addAll(s.optString("provider_off").split(",").map { it.trim() }.filter { it.isNotEmpty() }) } }
    val keys = remember(s) { mutableStateMapOf<String, String>() }          // khóa mới gõ (chỉ gửi nếu không rỗng)
    val models = remember(s) {
        mutableStateMapOf<String, String>().also { m -> for (p in Providers) if (p.modelField != null) m[p.name] = s.optString(p.modelField) }
    }
    var cloudflareOn by remember(s) { mutableStateOf(s.optBoolean("cloudflare_on", true)) }
    var cloudflareUrl by remember(s) { mutableStateOf(s.optString("cloudflare_url")) }
    var failover by remember(s) { mutableStateOf(s.optBoolean("failover", true)) }
    var saveQuota by remember(s) { mutableStateOf(s.optBoolean("save_quota", true)) }
    var browser by remember(s) { mutableStateOf(s.optString("browser", "chrome")) }
    var bypassProxy by remember(s) { mutableStateOf(s.optBoolean("bypass_proxy", true)) }
    var language by remember(s) { mutableStateOf(s.optString("language", "Vietnamese")) }
    var nickname by remember(s) { mutableStateOf(s.optString("nickname")) }
    var wordiness by remember(s) { mutableStateOf(s.optInt("wordiness", 0)) }
    var personality by remember(s) { mutableStateOf(s.optString("personality")) }
    var allowInput by remember(s) { mutableStateOf(s.optBoolean("allow_input", true)) }
    var greet by remember(s) { mutableStateOf(s.optBoolean("greet", true)) }
    var customApps by remember(s) { mutableStateOf(s.optString("custom_apps")) }

    Section("Thứ tự dùng AI") {
        Text(
            "Capri thử từ trên xuống dưới. Nhà nào hết lượt (ví dụ Gemini miễn phí chỉ ~20 lượt/ngày) hoặc lỗi thì tự chuyển sang nhà kế tiếp, " +
                "và thử lại nhà đó sau một lúc. Dùng nút mũi tên để đổi thứ tự, công tắc để bật/tắt.",
            style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant
        )
        order.forEachIndexed { index, name ->
            val info = Providers.firstOrNull { it.name == name } ?: return@forEachIndexed
            val configured = if (info.keyField == null) cloudflareOn else (s.optBoolean(info.keyField + "_set") || !keys[name].isNullOrBlank())
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text("${index + 1}.", Modifier.width(26.dp), style = MaterialTheme.typography.titleMedium)
                Column(Modifier.weight(1f)) {
                    Text(info.label, style = MaterialTheme.typography.titleMedium)
                    Text(
                        if (name in off) "đang tắt" else if (!configured) (if (info.keyField == null) "chưa bật" else "chưa có khóa") else "sẵn sàng",
                        style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
                IconButton(enabled = index > 0, onClick = { order.removeAt(index); order.add(index - 1, name) }) { Icon(Icons.Outlined.ArrowUpward, "Lên") }
                IconButton(enabled = index < order.lastIndex, onClick = { order.removeAt(index); order.add(index + 1, name) }) { Icon(Icons.Outlined.ArrowDownward, "Xuống") }
                Switch(checked = name !in off, onCheckedChange = { on -> if (on) off.remove(name) else if (name !in off) off.add(name) })
            }
        }
        SwitchRow("Tự chuyển nhà cung cấp khi lỗi", "Tắt thì chỉ dùng nhà đứng đầu danh sách.", failover) { failover = it }
        SwitchRow("Tiết kiệm lượt AI", "Làm xong việc là kết thúc, không gọi AI lần hai chỉ để nói \"xong rồi\".", saveQuota) { saveQuota = it }
    }

    for (info in Providers) {
        if (info.keyField == null) {
            Section(info.label + " (không cần khóa)") {
                SwitchRow("Dùng Cloudflare Worker", "Hiểu lệnh điều khiển máy (mở app, web, âm lượng...), không trò chuyện được. ~1.000 lệnh/ngày, reset 07:00 sáng.", cloudflareOn) { cloudflareOn = it }
                OutlinedTextField(value = cloudflareUrl, onValueChange = { cloudflareUrl = it }, singleLine = true, label = { Text("Địa chỉ Worker") }, colors = fieldColors(), modifier = Modifier.fillMaxWidth())
            }
        } else {
            Section(info.label) {
                SecretField(
                    "Khóa API " + info.label, s.optBoolean(info.keyField + "_set"), s.optString(info.keyField + "_hint"),
                    keys[info.name] ?: "", { keys[info.name] = it },
                    onClear = { vm.saveSettings(JSONObject().put(info.keyField, "")) }
                )
                ModelPicker(
                    models[info.name] ?: "", { models[info.name] = it }, state.models[info.name] ?: emptyList(),
                    { vm.fetchModels(info.name, keys[info.name] ?: "") }, state.modelsNote
                )
            }
        }
    }

    Section("Trình duyệt") {
        Text("Capri mở web và YouTube bằng trình duyệt này. Không tìm thấy thì báo rõ, không âm thầm mở Internet Explorer.", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        val isCustom = browser !in listOf("chrome", "firefox", "default")
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            FilterChip(selected = browser == "chrome", onClick = { browser = "chrome" }, label = { Text("Chrome") })
            FilterChip(selected = browser == "firefox", onClick = { browser = "firefox" }, label = { Text("Firefox") })
            FilterChip(selected = browser == "default", onClick = { browser = "default" }, label = { Text("Mặc định Windows") })
            FilterChip(selected = isCustom, onClick = { if (!isCustom) browser = "C:\\Program Files\\..." }, label = { Text("Tự chọn") })
        }
        if (isCustom) {
            OutlinedTextField(
                value = browser, onValueChange = { browser = it }, singleLine = true, label = { Text("Đường dẫn trình duyệt (.exe)") },
                colors = fieldColors(), modifier = Modifier.fillMaxWidth()
            )
        }
        OutlinedButton(onClick = { vm.quick("Đặt Chrome làm trình duyệt mặc định", "set_default_browser", JSONObject().put("browser", "chrome")); vm.go(Screen.Chat) }) {
            Text("Đặt Chrome làm trình duyệt mặc định")
        }
        SwitchRow("Bỏ qua proxy khi gọi AI", "Nhanh hơn trên Windows 7. Tắt nếu máy bạn bắt buộc đi qua proxy.", bypassProxy) { bypassProxy = it }
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
                .put("provider_order", order.joinToString(",")).put("provider_off", off.joinToString(","))
                .put("failover", failover).put("save_quota", saveQuota)
                .put("cloudflare_on", cloudflareOn).put("cloudflare_url", cloudflareUrl)
                .put("browser", browser).put("bypass_proxy", bypassProxy)
                .put("language", language).put("nickname", nickname).put("wordiness", wordiness).put("personality", personality)
                .put("greet", greet).put("allow_input", allowInput).put("custom_apps", customApps)
            for (p in Providers) {
                if (p.modelField != null) values.put(p.modelField, models[p.name] ?: "")
                // Khóa chỉ gửi khi người dùng vừa nhập khóa mới.
                val typed = keys[p.name]
                if (p.keyField != null && !typed.isNullOrBlank()) values.put(p.keyField, typed.trim())
            }
            vm.saveSettings(values)
        },
        modifier = Modifier.fillMaxWidth().height(50.dp)
    ) { Text("Lưu vào máy tính") }
}
