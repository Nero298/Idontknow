package app.capri.remote.ui

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.consumeWindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Apps
import androidx.compose.material.icons.outlined.CleaningServices
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material.icons.outlined.Language
import androidx.compose.material.icons.outlined.Lock
import androidx.compose.material.icons.outlined.Menu
import androidx.compose.material.icons.outlined.MusicNote
import androidx.compose.material.icons.outlined.PhotoCamera
import androidx.compose.material.icons.outlined.Settings
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.AssistChip
import androidx.compose.material3.DrawerValue
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.ModalDrawerSheet
import androidx.compose.material3.ModalNavigationDrawer
import androidx.compose.material3.NavigationDrawerItem
import androidx.compose.material3.NavigationDrawerItemDefaults
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.material3.rememberDrawerState
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import app.capri.remote.R
import app.capri.remote.net.ConnState
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import org.json.JSONObject
import java.util.Calendar

private enum class QuickKind { Web, App, Youtube, Clean, Shot, Music, Lock }

private data class QuickAction(val kind: QuickKind, val label: String, val icon: ImageVector)

private val QuickActions = listOf(
    QuickAction(QuickKind.Youtube, "Bật nhạc YouTube", Icons.Outlined.MusicNote),
    QuickAction(QuickKind.Web, "Mở web", Icons.Outlined.Language),
    QuickAction(QuickKind.App, "Mở app", Icons.Outlined.Apps),
    QuickAction(QuickKind.Clean, "Dọn rác", Icons.Outlined.CleaningServices),
    QuickAction(QuickKind.Shot, "Chụp màn hình", Icons.Outlined.PhotoCamera),
    QuickAction(QuickKind.Lock, "Khoá máy", Icons.Outlined.Lock)
)

private val CommonApps = listOf("Chrome", "Discord", "Spotify", "Explorer", "Notepad", "Calculator", "Firefox", "Zalo", "VS Code", "Task Manager")

@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
fun ChatScreen(vm: AppViewModel, state: UiState) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val drawerState = rememberDrawerState(DrawerValue.Closed)
    val conversation = state.conversations.firstOrNull { it.id == state.currentId }
    val messages = conversation?.messages ?: emptyList()
    val online = state.conn is ConnState.Online

    var input by rememberSaveable { mutableStateOf("") }
    var sheet by remember { mutableStateOf(false) }
    var dialog by remember { mutableStateOf<QuickKind?>(null) }
    var seconds by remember { mutableIntStateOf(0) }

    val micPermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) vm.startRecording()
    }
    fun tapMic() {
        val ok = ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED
        if (ok) vm.startRecording() else micPermission.launch(Manifest.permission.RECORD_AUDIO)
    }
    fun onQuick(kind: QuickKind) {
        sheet = false
        when (kind) {
            QuickKind.Shot -> vm.quick("Chụp màn hình", "screenshot")
            QuickKind.Lock -> vm.quick("Khoá máy", "lock_screen")
            else -> dialog = kind
        }
    }

    LaunchedEffect(state.recording) {
        while (state.recording) {
            seconds = vm.recordingSeconds()
            delay(300)
        }
        seconds = 0
    }

    val listState = rememberLazyListState()
    LaunchedEffect(messages.size, messages.lastOrNull()?.text, messages.lastOrNull()?.pending) {
        if (messages.isNotEmpty()) listState.animateScrollToItem(messages.lastIndex)
    }

    ModalNavigationDrawer(
        drawerState = drawerState,
        drawerContent = {
            ModalDrawerSheet(drawerContainerColor = MaterialTheme.colorScheme.background) {
                Row(Modifier.padding(horizontal = 24.dp, vertical = 20.dp), verticalAlignment = Alignment.CenterVertically) {
                    Image(painterResource(R.drawable.capri_avatar), null, Modifier.size(34.dp).clip(CircleShape))
                    Spacer(Modifier.width(12.dp))
                    Text("Capri", style = MaterialTheme.typography.titleLarge)
                }
                NavigationDrawerItem(
                    label = { Text("Cuộc trò chuyện mới") },
                    icon = { Icon(Icons.Outlined.Edit, null) },
                    selected = false,
                    onClick = { vm.newChat(); scope.launch { drawerState.close() } },
                    modifier = Modifier.padding(NavigationDrawerItemDefaults.ItemPadding)
                )
                Text(
                    "Gần đây", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(start = 28.dp, top = 16.dp, bottom = 6.dp)
                )
                LazyColumn(Modifier.weight(1f)) {
                    items(state.conversations, key = { it.id }) { c ->
                        NavigationDrawerItem(
                            label = { Text(c.title, maxLines = 1, overflow = TextOverflow.Ellipsis) },
                            selected = c.id == state.currentId,
                            onClick = { vm.selectChat(c.id); scope.launch { drawerState.close() } },
                            badge = {
                                IconButton(onClick = { vm.deleteChat(c.id) }, modifier = Modifier.size(32.dp)) {
                                    Icon(Icons.Outlined.Delete, "Xoá", Modifier.size(18.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
                                }
                            },
                            colors = NavigationDrawerItemDefaults.colors(
                                selectedContainerColor = MaterialTheme.colorScheme.surfaceVariant,
                                unselectedContainerColor = androidx.compose.ui.graphics.Color.Transparent
                            ),
                            modifier = Modifier.padding(NavigationDrawerItemDefaults.ItemPadding)
                        )
                    }
                }
                HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
                NavigationDrawerItem(
                    label = { Text("Cài đặt") },
                    icon = { Icon(Icons.Outlined.Settings, null) },
                    selected = false,
                    onClick = { vm.go(Screen.Settings); scope.launch { drawerState.close() } },
                    modifier = Modifier.padding(NavigationDrawerItemDefaults.ItemPadding).padding(vertical = 4.dp)
                )
            }
        }
    ) {
        Scaffold(
            containerColor = MaterialTheme.colorScheme.background,
            topBar = {
                TopAppBar(
                    title = {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text("Capri", style = MaterialTheme.typography.titleLarge)
                            Spacer(Modifier.width(10.dp))
                            StatusDot(online)
                        }
                    },
                    navigationIcon = { IconButton(onClick = { scope.launch { drawerState.open() } }) { Icon(Icons.Outlined.Menu, "Menu") } },
                    actions = { IconButton(onClick = { vm.newChat() }) { Icon(Icons.Outlined.Edit, "Cuộc trò chuyện mới") } },
                    colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.background)
                )
            }
        ) { padding ->
            Column(Modifier.padding(padding).consumeWindowInsets(padding).imePadding().fillMaxSize()) {
                if (!online) ConnectionBanner(state.conn, onClick = {
                    if (state.conn is ConnState.NeedsPairing || state.conn is ConnState.Failed || state.host.isBlank()) vm.go(Screen.Connect) else vm.reconnect()
                })
                Box(Modifier.weight(1f).fillMaxWidth()) {
                    if (messages.isEmpty()) {
                        EmptyState()
                    } else {
                        LazyColumn(
                            state = listState,
                            contentPadding = PaddingValues(horizontal = 16.dp, vertical = 12.dp),
                            verticalArrangement = Arrangement.spacedBy(20.dp)
                        ) {
                            items(messages, key = { it.id }) { MessageItem(it) }
                        }
                    }
                }
                if (!state.recording) {
                    LazyRow(
                        contentPadding = PaddingValues(horizontal = 14.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        items(QuickActions) { action ->
                            AssistChip(
                                onClick = { onQuick(action.kind) },
                                label = { Text(action.label) },
                                leadingIcon = { Icon(action.icon, null, Modifier.size(18.dp)) }
                            )
                        }
                    }
                }
                Composer(
                    text = input,
                    onText = { input = it },
                    recording = state.recording,
                    seconds = seconds,
                    onSend = { vm.sendText(input); input = "" },
                    onMic = { tapMic() },
                    onStopAndSend = { vm.stopAndSend() },
                    onCancelRecording = { vm.cancelRecording() },
                    onPlus = { sheet = true }
                )
            }
        }
    }

    if (sheet) {
        ModalBottomSheet(onDismissRequest = { sheet = false }, sheetState = rememberModalBottomSheetState(), containerColor = MaterialTheme.colorScheme.background) {
            Column(Modifier.padding(horizontal = 20.dp).padding(bottom = 28.dp)) {
                Text("Hành động nhanh", style = MaterialTheme.typography.titleLarge)
                Spacer(Modifier.height(12.dp))
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    for (action in QuickActions) {
                        AssistChip(onClick = { onQuick(action.kind) }, label = { Text(action.label) }, leadingIcon = { Icon(action.icon, null, Modifier.size(18.dp)) })
                    }
                }
                Spacer(Modifier.height(16.dp))
                Text("Nhạc & âm lượng", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
                Spacer(Modifier.height(8.dp))
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    val media = listOf(
                        Triple("Trước", "media", "previous"), Triple("Phát / Dừng", "media", "play_pause"), Triple("Sau", "media", "next"),
                    )
                    for ((label, tool, arg) in media) {
                        AssistChip(onClick = { sheet = false; vm.quick(label, tool, JSONObject().put("action", arg)) }, label = { Text(label) })
                    }
                    val volume = listOf(Pair("Âm lượng −", "volume_down"), Pair("Âm lượng +", "volume_up"), Pair("Tắt tiếng", "mute"))
                    for ((label, arg) in volume) {
                        AssistChip(onClick = { sheet = false; vm.quick(label, "volume", JSONObject().put("action", arg)) }, label = { Text(label) })
                    }
                }
            }
        }
    }

    when (dialog) {
        QuickKind.Web -> TextInputDialog(
            title = "Mở trang web", hint = "youtube.com hoặc từ khoá tìm kiếm", confirm = "Mở",
            onDismiss = { dialog = null },
            onConfirm = { text -> dialog = null; vm.quick("Mở web: $text", "open_website", JSONObject().put("url", text)) }
        )
        QuickKind.Youtube -> TextInputDialog(
            title = "Bật nhạc trên YouTube", hint = "Tên bài hát, ví dụ: Love Me Not", confirm = "Phát",
            onDismiss = { dialog = null },
            onConfirm = { text -> dialog = null; vm.quick("Bật nhạc: $text", "play_youtube", JSONObject().put("query", text).put("browser", "chrome")) }
        )
        QuickKind.App -> AppDialog(
            onDismiss = { dialog = null },
            onPick = { name -> dialog = null; vm.quick("Mở $name", "open_app", JSONObject().put("name", name)) }
        )
        QuickKind.Clean -> AlertDialog(
            onDismissRequest = { dialog = null },
            title = { Text("Dọn dẹp máy tính?") },
            text = { Text("Xoá file tạm trong thư mục Temp (bỏ qua file đang dùng) và đổ thùng rác.") },
            confirmButton = { TextButton(onClick = { dialog = null; vm.quick("Dọn rác", "cleanup", JSONObject().put("what", "all")) }) { Text("Dọn") } },
            dismissButton = { TextButton(onClick = { dialog = null }) { Text("Huỷ") } }
        )
        else -> Unit
    }

    val confirm = state.confirm
    if (confirm != null) {
        AlertDialog(
            onDismissRequest = { vm.answerConfirm(false) },
            title = { Text("Capri hỏi") },
            text = { Text(confirm.text) },
            confirmButton = { TextButton(onClick = { vm.answerConfirm(true) }) { Text("Đồng ý") } },
            dismissButton = { TextButton(onClick = { vm.answerConfirm(false) }) { Text("Không") } }
        )
    }
}

@Composable
private fun ConnectionBanner(conn: ConnState, onClick: () -> Unit) {
    val text = when (conn) {
        is ConnState.Connecting -> "Đang kết nối với máy tính…"
        is ConnState.NeedsPairing -> "Cần ghép đôi với máy tính. Chạm để nhập mã."
        is ConnState.Failed -> conn.message
        else -> "Mất kết nối với máy tính. Chạm để thử lại."
    }
    Surface(
        color = MaterialTheme.colorScheme.primaryContainer,
        modifier = Modifier.fillMaxWidth().clickable(onClick = onClick)
    ) {
        Text(text, Modifier.padding(horizontal = 16.dp, vertical = 9.dp), style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onPrimaryContainer)
    }
}

@Composable
private fun EmptyState() {
    val hour = Calendar.getInstance().get(Calendar.HOUR_OF_DAY)
    val greeting = when {
        hour in 5..11 -> "Chào buổi sáng"
        hour in 12..17 -> "Chào buổi chiều"
        hour in 18..21 -> "Chào buổi tối"
        else -> "Khuya rồi đó"
    }
    Column(Modifier.fillMaxSize().padding(32.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
        Image(painterResource(R.drawable.capri_hero), null, Modifier.size(150.dp))
        Spacer(Modifier.height(12.dp))
        Text(greeting, style = MaterialTheme.typography.headlineMedium, textAlign = TextAlign.Center)
        Spacer(Modifier.height(6.dp))
        Text("Capri đây. Hôm nay quậy gì nào?", style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onSurfaceVariant, textAlign = TextAlign.Center)
    }
}

@Composable
private fun TextInputDialog(title: String, hint: String, confirm: String, onDismiss: () -> Unit, onConfirm: (String) -> Unit) {
    var text by remember { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = {
            OutlinedTextField(
                value = text, onValueChange = { text = it }, singleLine = true,
                placeholder = { Text(hint) }, keyboardOptions = KeyboardOptions.Default,
                modifier = Modifier.fillMaxWidth()
            )
        },
        confirmButton = { TextButton(enabled = text.isNotBlank(), onClick = { onConfirm(text.trim()) }) { Text(confirm) } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Huỷ") } }
    )
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun AppDialog(onDismiss: () -> Unit, onPick: (String) -> Unit) {
    var custom by remember { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Mở ứng dụng") },
        text = {
            Column {
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    for (name in CommonApps) FilterChip(selected = false, onClick = { onPick(name) }, label = { Text(name) })
                }
                Spacer(Modifier.height(12.dp))
                OutlinedTextField(
                    value = custom, onValueChange = { custom = it }, singleLine = true,
                    placeholder = { Text("Hoặc gõ tên ứng dụng khác") }, modifier = Modifier.fillMaxWidth()
                )
            }
        },
        confirmButton = { TextButton(enabled = custom.isNotBlank(), onClick = { onPick(custom.trim()) }) { Text("Mở") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Huỷ") } }
    )
}
