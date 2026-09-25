package com.husarp.linkpilot

import android.app.StatusBarManager
import android.app.role.RoleManager
import android.content.ClipData
import android.content.ClipboardManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.annotation.RequiresApi
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.ui.res.painterResource
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Build
import androidx.compose.material.icons.filled.DateRange
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.List
import androidx.compose.material.icons.filled.Star
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Checkbox
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.material3.dynamicLightColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.core.graphics.drawable.toBitmap
import androidx.compose.ui.graphics.ImageBitmap
import android.os.Handler
import android.os.Looper
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Executors

// What the screens share: the settings, and a counter that goes up whenever something changed - on
// a screen, a tile, or by coming back to the app - so every screen shows what is true now.
//
// What is slow to find out - app names and icons, which browser is the default, the link log - is
// looked up away from the screen when the app starts (and again after a change) and kept here, so a
// tab only shows what is already known instead of asking Android while it is being drawn.
class Model(val app: Context) {
    val store = Store(app)
    var tick by mutableIntStateOf(0)
    var askedForDefault = false   // the setup's step 3 sent you to Android's question

    var isDefault by mutableStateOf(Browsers.isDefault(app)); private set
    var currentDefault by mutableStateOf<String?>(null); private set   // the browser links go to instead, by name
    var log by mutableStateOf<List<LogEntry>?>(null); private set       // null: still being read
    private var installed by mutableStateOf<Set<Category>?>(null)   // null: not checked yet - taken as there
    fun isInstalled(c: Category) = installed?.contains(c) ?: true
    private val labels = ConcurrentHashMap<String, String>()
    private val icons = ConcurrentHashMap<String, ImageBitmap>()
    private val worker = Executors.newSingleThreadExecutor()
    private val main = Handler(Looper.getMainLooper())

    fun changed() { tick++; Tiles.refresh(app); reload() }

    fun reload() {
        worker.execute {
            val isDef = Browsers.isDefault(app)
            val other = if (isDef) null else Browsers.currentDefault(app)?.let { appLabel(it) }
            val cats = store.categories
            val there = cats.filter { Browsers.installed(app, it) }.toSet()
            cats.forEach { label(it); icon(it) }
            val entries = store.readLog()
            entries.map { it.from }.distinct().forEach { if (it.isNotEmpty()) { appLabel(it); appIcon(it) } }
            main.post { isDefault = isDef; currentDefault = other; installed = there; log = entries }
        }
    }

    private fun key(c: Category) = c.pkg + "|" + (c.profile ?: "")
    fun label(c: Category): String = labels.getOrPut("c:" + key(c)) { Browsers.label(app, c) }
    fun appLabel(pkg: String): String = labels.getOrPut(pkg) { Browsers.label(app, pkg) }
    fun icon(c: Category): ImageBitmap? =
        icons[key(c)] ?: Browsers.icon(app, c)?.toBitmap(96, 96)?.asImageBitmap()?.also { icons[key(c)] = it }
    // an app's icon, for the link log - null for one that is gone
    fun appIcon(pkg: String): ImageBitmap? = icons["a:$pkg"] ?: try {
        app.packageManager.getApplicationIcon(pkg).toBitmap(64, 64).asImageBitmap().also { icons["a:$pkg"] = it }
    } catch (_: Exception) { null }
}

class MainActivity : ComponentActivity() {
    private lateinit var model: Model
    private val askRole = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { model.changed() }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        model = Model(applicationContext)
        makeFirstCategory()   // what is slow to look up is read in onResume, which always follows
        setContent { App(model, ::makeDefault, ::openDefaultSettings) }
    }

    override fun onResume() {
        super.onResume()
        model.changed()
    }

    // With no category yet, the browser used until now becomes the first one - live - so links keep
    // going where they went.
    private fun makeFirstCategory() {
        val store = model.store
        if (store.categories.isNotEmpty()) return
        val pkg = Browsers.currentDefault(this) ?: Browsers.all(this).firstOrNull()?.pkg ?: return
        val name = Browsers.label(this, pkg)
        store.categories = listOf(Category(name, pkg))
        store.active = name
    }

    // Android asks the person to confirm - no app can make itself the default on its own.
    private fun makeDefault() {
        if (Build.VERSION.SDK_INT >= 29) {
            val roles = getSystemService(RoleManager::class.java)
            if (roles != null && roles.isRoleAvailable(RoleManager.ROLE_BROWSER) && !roles.isRoleHeld(RoleManager.ROLE_BROWSER)) {
                askRole.launch(roles.createRequestRoleIntent(RoleManager.ROLE_BROWSER))
                return
            }
        }
        openDefaultSettings()
    }

    private fun openDefaultSettings() {
        try { startActivity(Intent(Settings.ACTION_MANAGE_DEFAULT_APPS_SETTINGS)) }
        catch (_: Exception) { startActivity(Intent(Settings.ACTION_SETTINGS)) }
    }
}

@Composable
fun App(m: Model, makeDefault: () -> Unit, openSettings: () -> Unit) {
    val ctx = LocalContext.current
    val dark = isSystemInDarkTheme()
    val colors = when {
        Build.VERSION.SDK_INT >= 31 -> if (dark) dynamicDarkColorScheme(ctx) else dynamicLightColorScheme(ctx)
        dark -> darkColorScheme()
        else -> lightColorScheme()
    }
    MaterialTheme(colorScheme = colors) {
        var setup by rememberSaveable { mutableStateOf(!m.store.setupDone || !m.isDefault) }
        var tab by rememberSaveable { mutableIntStateOf(0) }
        val tabs = listOf("Home" to Icons.Default.Home, "Shortcuts" to Icons.Default.Star, "Rules" to Icons.Default.List,
                          "Cleaning" to Icons.Default.Build, "Log" to Icons.Default.DateRange)
        if (setup) Scaffold { pad ->
            Column(Modifier.padding(pad)) {
                SetupScreen(m, makeDefault, openSettings) { m.store.setupDone = true; setup = false; m.changed() }
            }
        } else Scaffold(bottomBar = {
            NavigationBar {
                tabs.forEachIndexed { i, (label, icon) ->
                    NavigationBarItem(selected = tab == i, onClick = { tab = i }, icon = { Icon(icon, null) }, label = { Text(label) })
                }
            }
        }) { pad ->
            Column(Modifier.padding(pad)) {
                when (tab) {
                    0 -> HomeTab(m, makeDefault, openSettings) { setup = true }
                    1 -> ShortcutsTab(m)
                    2 -> RulesTab(m)
                    3 -> CleaningTab(m)
                    else -> LogTab(m)
                }
            }
        }
    }
}
