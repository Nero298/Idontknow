package app.capri.remote

import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import app.capri.remote.ui.AppViewModel
import app.capri.remote.ui.CapriTheme
import app.capri.remote.ui.ChatScreen
import app.capri.remote.ui.ConnectScreen
import app.capri.remote.ui.Screen
import app.capri.remote.ui.SettingsScreen

class MainActivity : ComponentActivity() {
    private val vm: AppViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            val state by vm.ui.collectAsStateWithLifecycle()
            CapriTheme(state.theme) {
                val context = LocalContext.current
                LaunchedEffect(state.toast) {
                    val message = state.toast
                    if (message != null) {
                        Toast.makeText(context, message, Toast.LENGTH_LONG).show()
                        vm.consumeToast()
                    }
                }
                BackHandler(enabled = state.screen != Screen.Chat && state.host.isNotBlank()) { vm.go(Screen.Chat) }
                Surface(color = MaterialTheme.colorScheme.background) {
                    when (state.screen) {
                        Screen.Chat -> ChatScreen(vm, state)
                        Screen.Connect -> ConnectScreen(vm, state)
                        Screen.Settings -> SettingsScreen(vm, state)
                    }
                }
            }
        }
    }
}
