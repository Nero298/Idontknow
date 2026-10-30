package app.capri.remote.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

/*
 * Bảng màu và chữ lấy cảm hứng từ bố cục của Claude: nền kem ấm, chữ serif cho lời chào,
 * màu nhấn cam đất, ô nhập bo tròn. Đây chỉ là phong cách giao diện, không dùng logo hay thương hiệu của Claude.
 */
private val LightColors = lightColorScheme(
    primary = Color(0xFFC96442),
    onPrimary = Color(0xFFFFFFFF),
    primaryContainer = Color(0xFFF4DDD3),
    onPrimaryContainer = Color(0xFF5A2A18),
    secondary = Color(0xFF6B6A64),
    secondaryContainer = Color(0xFFF0EEE6),
    onSecondaryContainer = Color(0xFF1F1E1D),
    background = Color(0xFFFAF9F5),
    onBackground = Color(0xFF1F1E1D),
    surface = Color(0xFFFFFFFF),
    onSurface = Color(0xFF1F1E1D),
    surfaceVariant = Color(0xFFF0EEE6),
    onSurfaceVariant = Color(0xFF6B6A64),
    outline = Color(0xFFBDBAAE),
    outlineVariant = Color(0xFFE6E3D8),
    error = Color(0xFFB3261E),
    onError = Color(0xFFFFFFFF)
)

private val DarkColors = darkColorScheme(
    primary = Color(0xFFD97757),
    onPrimary = Color(0xFF1F1E1D),
    primaryContainer = Color(0xFF5A3426),
    onPrimaryContainer = Color(0xFFF4DDD3),
    secondary = Color(0xFFA8A69E),
    secondaryContainer = Color(0xFF3A3935),
    onSecondaryContainer = Color(0xFFF0EEE6),
    background = Color(0xFF262624),
    onBackground = Color(0xFFF0EEE6),
    surface = Color(0xFF30302E),
    onSurface = Color(0xFFF0EEE6),
    surfaceVariant = Color(0xFF3B3A36),
    onSurfaceVariant = Color(0xFFA8A69E),
    outline = Color(0xFF6B6A64),
    outlineVariant = Color(0xFF45443F),
    error = Color(0xFFF2B8B5),
    onError = Color(0xFF601410)
)

private val CapriTypography = Typography(
    headlineMedium = TextStyle(fontFamily = FontFamily.Serif, fontWeight = FontWeight.Normal, fontSize = 30.sp, lineHeight = 38.sp),
    titleLarge = TextStyle(fontFamily = FontFamily.Serif, fontWeight = FontWeight.Medium, fontSize = 22.sp, lineHeight = 28.sp),
    titleMedium = TextStyle(fontWeight = FontWeight.Medium, fontSize = 16.sp, lineHeight = 22.sp),
    bodyLarge = TextStyle(fontSize = 16.sp, lineHeight = 24.sp),
    bodyMedium = TextStyle(fontSize = 14.sp, lineHeight = 20.sp),
    labelLarge = TextStyle(fontWeight = FontWeight.Medium, fontSize = 14.sp)
)

@Composable
fun CapriTheme(mode: String, content: @Composable () -> Unit) {
    val dark = when (mode) {
        "dark" -> true
        "light" -> false
        else -> isSystemInDarkTheme()
    }
    MaterialTheme(
        colorScheme = if (dark) DarkColors else LightColors,
        typography = CapriTypography,
        content = content
    )
}
