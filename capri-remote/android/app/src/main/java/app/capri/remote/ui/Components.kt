package app.capri.remote.ui

import android.graphics.BitmapFactory
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowUpward
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Mic
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.IconButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.capri.remote.R
import app.capri.remote.data.ChatMessage

/** Chấm trạng thái online/offline cạnh tên Capri. */
@Composable
fun StatusDot(online: Boolean, modifier: Modifier = Modifier) {
    val color = if (online) androidx.compose.ui.graphics.Color(0xFF3FA66B) else MaterialTheme.colorScheme.outline
    Box(modifier.size(9.dp).clip(CircleShape).background(color))
}

@Composable
fun ThinkingDots() {
    val transition = rememberInfiniteTransition(label = "dots")
    val alpha by transition.animateFloat(
        initialValue = 0.25f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(650), RepeatMode.Reverse),
        label = "alpha"
    )
    Row(Modifier.padding(vertical = 8.dp)) {
        repeat(3) {
            Box(
                Modifier
                    .padding(end = 5.dp)
                    .size(7.dp)
                    .alpha(alpha)
                    .background(MaterialTheme.colorScheme.primary, CircleShape)
            )
        }
    }
}

/** Một tin nhắn: của người dùng là bong bóng xám nhạt bên phải, của Capri là chữ phẳng kèm avatar (như Claude). */
@Composable
fun MessageItem(message: ChatMessage) {
    if (message.role == "user") {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
            Surface(
                color = MaterialTheme.colorScheme.surfaceVariant,
                shape = RoundedCornerShape(20.dp),
                modifier = Modifier.widthIn(max = 310.dp)
            ) {
                Text(
                    (if (message.voice) "🎙 " else "") + message.text,
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 11.dp),
                    style = MaterialTheme.typography.bodyLarge,
                    color = MaterialTheme.colorScheme.onSurface
                )
            }
        }
        return
    }

    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.Top) {
        Image(
            painterResource(R.drawable.capri_avatar), null,
            Modifier.padding(top = 2.dp).size(28.dp).clip(CircleShape),
            contentScale = ContentScale.Crop
        )
        Spacer(Modifier.width(12.dp))
        Column(Modifier.weight(1f)) {
            if (message.pending) {
                ThinkingDots()
            } else {
                if (message.text.isNotEmpty()) {
                    SelectionContainer {
                        Text(
                            message.text,
                            style = MaterialTheme.typography.bodyLarge,
                            color = if (message.error) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onBackground
                        )
                    }
                }
                for (note in message.notes) {
                    Surface(
                        color = MaterialTheme.colorScheme.surfaceVariant,
                        shape = RoundedCornerShape(10.dp),
                        modifier = Modifier.padding(top = 8.dp)
                    ) {
                        Text(
                            (if (note.ok) "✓  " else "✗  ") + note.text,
                            modifier = Modifier.padding(horizontal = 10.dp, vertical = 6.dp),
                            style = MaterialTheme.typography.bodyMedium,
                            color = if (note.ok) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.error
                        )
                    }
                }
                val imagePath = message.image
                if (imagePath != null) {
                    val bitmap = remember(imagePath) {
                        runCatching { BitmapFactory.decodeFile(imagePath)?.asImageBitmap() }.getOrNull()
                    }
                    if (bitmap != null) {
                        Image(
                            bitmap, "Ảnh chụp màn hình",
                            Modifier.padding(top = 10.dp).fillMaxWidth().clip(RoundedCornerShape(14.dp)),
                            contentScale = ContentScale.FillWidth
                        )
                    }
                }
            }
        }
    }
}

/** Ô nhập bo tròn kiểu Claude: chữ ở trên, hàng nút (+ ... mic/gửi) ở dưới. Khi đang ghi âm thì thành thanh "Đang nghe". */
@Composable
fun Composer(
    text: String,
    onText: (String) -> Unit,
    recording: Boolean,
    seconds: Int,
    onSend: () -> Unit,
    onMic: () -> Unit,
    onStopAndSend: () -> Unit,
    onCancelRecording: () -> Unit,
    onPlus: () -> Unit
) {
    Surface(
        shape = RoundedCornerShape(28.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant),
        shadowElevation = 1.dp,
        modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 8.dp)
    ) {
        if (recording) {
            Row(
                Modifier.padding(start = 20.dp, end = 8.dp, top = 8.dp, bottom = 8.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Box(Modifier.size(10.dp).background(MaterialTheme.colorScheme.error, CircleShape))
                Spacer(Modifier.width(10.dp))
                Text(
                    "Đang nghe…  %d:%02d".format(seconds / 60, seconds % 60),
                    style = MaterialTheme.typography.bodyLarge,
                    color = MaterialTheme.colorScheme.onSurface,
                    modifier = Modifier.weight(1f)
                )
                IconButton(onClick = onCancelRecording) { Icon(Icons.Outlined.Close, "Huỷ ghi âm") }
                FilledIconButton(
                    onClick = onStopAndSend,
                    colors = IconButtonDefaults.filledIconButtonColors(containerColor = MaterialTheme.colorScheme.primary)
                ) { Icon(Icons.Filled.ArrowUpward, "Gửi giọng nói") }
            }
        } else {
            Column(Modifier.padding(start = 18.dp, end = 8.dp, top = 12.dp, bottom = 6.dp)) {
                BasicTextField(
                    value = text,
                    onValueChange = onText,
                    textStyle = TextStyle(color = MaterialTheme.colorScheme.onSurface, fontSize = 16.sp, lineHeight = 22.sp),
                    cursorBrush = SolidColor(MaterialTheme.colorScheme.primary),
                    maxLines = 6,
                    modifier = Modifier.fillMaxWidth().padding(end = 10.dp),
                    decorationBox = { inner ->
                        Box {
                            if (text.isEmpty()) {
                                Text("Nhắn cho Capri", style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            inner()
                        }
                    }
                )
                Spacer(Modifier.height(6.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    IconButton(onClick = onPlus) { Icon(Icons.Outlined.Add, "Hành động nhanh", tint = MaterialTheme.colorScheme.onSurfaceVariant) }
                    Spacer(Modifier.weight(1f))
                    if (text.isBlank()) {
                        IconButton(onClick = onMic) { Icon(Icons.Outlined.Mic, "Nói với Capri", tint = MaterialTheme.colorScheme.onSurfaceVariant) }
                    } else {
                        FilledIconButton(
                            onClick = onSend,
                            colors = IconButtonDefaults.filledIconButtonColors(containerColor = MaterialTheme.colorScheme.primary)
                        ) { Icon(Icons.Filled.ArrowUpward, "Gửi") }
                    }
                }
            }
        }
    }
}
