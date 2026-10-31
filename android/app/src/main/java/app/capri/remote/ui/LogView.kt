package app.capri.remote.ui

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.consumeWindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.capri.remote.data.LogFormat
import app.capri.remote.data.LogItem

private fun levelColor(level: String, scheme: androidx.compose.material3.ColorScheme): Color = when (level) {
    "ERROR" -> scheme.error
    "WARN" -> Color(0xFFB7791F)
    else -> scheme.onSurfaceVariant
}

/**
 * Chat cố định "Nhật ký lỗi": dòng log của Host + điện thoại, có mức (màu), lọc, Sao chép tất cả (kèm phần đầu phiên bản/giờ/mô hình AI),
 * Chia sẻ, Xoá. Mở ra là tự hỏi Host log mới nhất.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LogScreen(vm: AppViewModel, state: UiState) {
    val context = LocalContext.current
    var level by remember { mutableStateOf("ALL") }       // ALL | ERROR | WARN | INFO
    var confirmClear by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) { vm.refreshLog() }

    val shown = state.logs.filter { item ->
        (level == "ALL" || item.level == level) && (state.logCmd == null || item.cmd == state.logCmd)
    }
    val listState = rememberLazyListState()
    LaunchedEffect(shown.size) { if (shown.isNotEmpty()) listState.scrollToItem(shown.lastIndex) }

    fun report(): String = vm.logReport(shown)

    Scaffold(
        containerColor = MaterialTheme.colorScheme.background,
        topBar = {
            TopAppBar(
                title = { Text("Nhật ký lỗi", style = MaterialTheme.typography.titleLarge) },
                navigationIcon = { IconButton(onClick = { vm.go(Screen.Chat) }) { Icon(Icons.AutoMirrored.Outlined.ArrowBack, "Quay lại") } },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.background)
            )
        }
    ) { padding ->
        Column(Modifier.padding(padding).consumeWindowInsets(padding).fillMaxSize()) {
            Row(Modifier.padding(horizontal = 12.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                for ((value, label) in listOf("ALL" to "Tất cả", "ERROR" to "Lỗi", "WARN" to "Cảnh báo", "INFO" to "Thông tin")) {
                    FilterChip(selected = level == value, onClick = { level = value }, label = { Text(label) })
                }
            }
            if (state.logCmd != null) {
                Row(Modifier.padding(horizontal = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text("Chỉ lệnh ${state.logCmd}", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    TextButton(onClick = { vm.setLogCmd(null) }) { Text("Xem tất cả") }
                }
            }
            Row(Modifier.padding(horizontal = 12.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                OutlinedButton(onClick = {
                    val cm = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                    cm.setPrimaryClip(ClipData.newPlainText("Capri log", report()))
                    Toast.makeText(context, "Đã sao chép ${shown.size} dòng", Toast.LENGTH_SHORT).show()
                }) { Text("Sao chép tất cả") }
                OutlinedButton(onClick = {
                    val send = Intent(Intent.ACTION_SEND).setType("text/plain")
                        .putExtra(Intent.EXTRA_SUBJECT, "Nhật ký Capri").putExtra(Intent.EXTRA_TEXT, report())
                    context.startActivity(Intent.createChooser(send, "Chia sẻ nhật ký"))
                }) { Text("Chia sẻ") }
                OutlinedButton(onClick = { confirmClear = true }) { Text("Xoá") }
            }
            Box(Modifier.weight(1f).fillMaxWidth().padding(top = 8.dp)) {
                if (shown.isEmpty()) {
                    Text(
                        if (state.logs.isEmpty()) "Chưa có dòng nhật ký nào." else "Không có dòng nào khớp bộ lọc.",
                        modifier = Modifier.padding(24.dp), color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                } else {
                    SelectionContainer {
                        LazyColumn(state = listState, contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 12.dp, vertical = 4.dp)) {
                            items(shown) { LogRow(it) }
                        }
                    }
                }
            }
        }
    }

    if (confirmClear) {
        AlertDialog(
            onDismissRequest = { confirmClear = false },
            title = { Text("Xoá nhật ký?") },
            text = { Text("Xoá nhật ký trên cả máy tính và điện thoại.") },
            confirmButton = { TextButton(onClick = { confirmClear = false; vm.clearLog() }) { Text("Xoá") } },
            dismissButton = { TextButton(onClick = { confirmClear = false }) { Text("Huỷ") } }
        )
    }
}

@Composable
private fun LogRow(item: LogItem) {
    val color = levelColor(item.level, MaterialTheme.colorScheme)
    Row(Modifier.fillMaxWidth().padding(vertical = 3.dp), verticalAlignment = Alignment.Top) {
        Box(Modifier.padding(top = 6.dp, end = 8.dp).size(7.dp).background(color, CircleShape))
        Column {
            Text(
                LogFormat.line(item, false),
                color = color, fontFamily = FontFamily.Monospace, fontSize = 12.sp, lineHeight = 16.sp
            )
            if (item.detail.isNotEmpty()) {
                Text(
                    item.detail, color = MaterialTheme.colorScheme.onSurfaceVariant,
                    fontFamily = FontFamily.Monospace, fontSize = 11.sp, lineHeight = 14.sp, maxLines = 12
                )
            }
        }
    }
}
