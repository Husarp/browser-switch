package com.husarp.linkpilot

import android.app.StatusBarManager
import android.content.ClipData
import android.content.ClipboardManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import android.widget.Toast
import androidx.annotation.RequiresApi
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.KeyboardArrowUp
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Checkbox
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.ListItemDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Switch
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
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.core.graphics.drawable.toBitmap
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

// The five tabs - Home, Shortcuts, Rules, Cleaning, Log. Every section is a card with a big title and
// one short line; the longer explanation sits behind its (i) button, as the "?" marks do on Windows.

// ---- building blocks -------------------------------------------------------------------------------

@Composable
fun PageTitle(text: String, line: String? = null) {
    Column(Modifier.padding(bottom = 4.dp)) {
        Text(text, style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.Bold)
        if (line != null) Text(line, style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

// A section: big title - with the (i) right beside it where there is more to say - one line under
// it, then what is in it.
@Composable
fun SectionCard(title: String, line: String? = null, info: List<String>? = null, content: @Composable ColumnScope.() -> Unit) {
    var open by rememberSaveable(title) { mutableStateOf(false) }
    Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerLow)) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Column {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(title, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold)
                    if (info != null) Icon(Icons.Outlined.Info, if (open) "Less" else "More about this",
                        tint = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.padding(start = 6.dp).clip(CircleShape).clickable { open = !open }.padding(4.dp).size(20.dp))
                }
                if (line != null) Text(line, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            if (open && info != null) InfoBox(info)
            content()
        }
    }
}

// The longer explanation: one point per line, its first words ("Offline:") in bold.
@Composable
fun InfoBox(points: List<String>) {
    Column(Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.secondaryContainer, RoundedCornerShape(12.dp)).padding(14.dp),
           verticalArrangement = Arrangement.spacedBy(8.dp)) {
        points.forEach { point ->
            val colon = point.indexOf(": ")
            Row {
                Text("•", color = MaterialTheme.colorScheme.onSecondaryContainer, modifier = Modifier.width(14.dp))
                Text(buildAnnotatedString {
                    if (colon in 1..30) {
                        withStyle(SpanStyle(fontWeight = FontWeight.Bold)) { append(point.substring(0, colon + 1)) }
                        append(point.substring(colon + 1))
                    } else append(point)
                }, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSecondaryContainer)
            }
        }
    }
}

@Composable
fun SwitchRow(title: String, text: String, on: Boolean, change: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().clickable { change(!on) }.padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.titleMedium)
            Text(text, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Spacer(Modifier.width(12.dp))
        Switch(checked = on, onCheckedChange = change)
    }
}

// Steps to take, one per line, each with its number in a circle.
@Composable
fun NumberedSteps(steps: List<String>) {
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        steps.forEachIndexed { i, step ->
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(30.dp).background(MaterialTheme.colorScheme.primary, CircleShape), contentAlignment = Alignment.Center) {
                    Text("${i + 1}", color = MaterialTheme.colorScheme.onPrimary, fontWeight = FontWeight.Bold)
                }
                Spacer(Modifier.width(12.dp))
                Text(step, style = MaterialTheme.typography.bodyLarge)
            }
        }
    }
}

@Composable
fun AppIcon(pkg: String) {
    val ctx = LocalContext.current
    val picture = remember(pkg) {
        try { ctx.packageManager.getApplicationIcon(pkg).toBitmap(96, 96).asImageBitmap() } catch (_: Exception) { null }
    }
    if (picture != null) Image(picture, null, Modifier.size(36.dp)) else Spacer(Modifier.size(36.dp))
}

// A category's browser icon - with the work badge if it is in the work profile. Kept by the Model.
@Composable
fun CategoryIcon(m: Model, c: Category) {
    val picture = remember(c) { m.icon(c) }
    if (picture != null) Image(picture, null, Modifier.size(36.dp)) else Spacer(Modifier.size(36.dp))
}

@Composable
fun CategoryPicker(names: List<String>, chosen: String?, choose: (String) -> Unit) {
    Column {
        names.forEach { n ->
            Row(Modifier.fillMaxWidth().clickable { choose(n) }, verticalAlignment = Alignment.CenterVertically) {
                RadioButton(selected = n == chosen, onClick = { choose(n) })
                Text(n)
            }
        }
    }
}

private val cardItem @Composable get() = ListItemDefaults.colors(containerColor = MaterialTheme.colorScheme.surfaceContainerLow)

// ---- Home: default browser, categories ------------------------------------------------------------

@Composable
fun HomeTab(m: Model, makeDefault: () -> Unit, openSettings: () -> Unit, showSetup: () -> Unit) {
    val ctx = LocalContext.current
    m.tick
    val store = m.store
    val cats = store.categories
    val live = store.live()
    val isDefault = m.isDefault
    var adding by remember { mutableStateOf(false) }

    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        item { PageTitle("LinkPilot") }
        item {
            if (isDefault) {
                Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.secondaryContainer)) {
                    Column(Modifier.fillMaxWidth().padding(16.dp)) {
                        Text("✓  Your links pass through LinkPilot", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                        Text("Cleaned, then opened in the live category's browser.", style = MaterialTheme.typography.bodyMedium)
                    }
                }
            } else {
                Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.errorContainer)) {
                    Column(Modifier.fillMaxWidth().padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text("Not your default browser yet", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                        val now = m.currentDefault
                        Text((if (now != null) "Links go straight to $now. " else "") +
                             "LinkPilot can only choose for links it gets. Android asks you to confirm.",
                             style = MaterialTheme.typography.bodyMedium)
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Button(onClick = makeDefault) { Text("Make it the default") }
                            TextButton(onClick = openSettings) { Text("Open settings") }
                        }
                    }
                }
            }
        }
        item {
            SectionCard("Links open in", "Tap a category to make it live") {
                if (cats.isEmpty()) Text("No categories yet.")
                cats.forEach { c ->
                    ListItem(
                        colors = cardItem,
                        modifier = Modifier.clickable { store.active = c.name; m.changed() },
                        leadingContent = { Row(verticalAlignment = Alignment.CenterVertically) {
                            RadioButton(selected = c.name == live?.name, onClick = { store.active = c.name; m.changed() })
                            CategoryIcon(m, c)
                        } },
                        headlineContent = { Text(c.name, fontWeight = FontWeight.Bold) },
                        supportingContent = { Text(if (m.isInstalled(c)) m.label(c) else "Browser not installed") },
                        trailingContent = {
                            IconButton(onClick = { store.categories = cats.filter { it.name != c.name }; m.changed() }) {
                                Icon(Icons.Default.Delete, "Remove")
                            }
                        })
                }
                OutlinedButton(onClick = { adding = true }) {
                    Icon(Icons.Default.Add, null); Spacer(Modifier.width(8.dp)); Text("Add a category")
                }
            }
        }
        item {
            SectionCard("About", "Works offline - no internet permission", info = listOf(
                "Offline: LinkPilot has no internet permission, so Android itself keeps it offline.",
                "Private: your categories, rules and link log stay on this phone.",
                "Some links never reach it: apps with their own built-in browser (Instagram, Facebook), some Google apps " +
                    "(they always use Chrome), and links an installed app opens itself (YouTube).",
                "Updates: LinkPilot cannot check by itself - it has no internet. The button opens the newest release on " +
                    "GitHub in your browser; if its number is higher than yours, download the APK there.")) {
                val version = remember { ctx.packageManager.getPackageInfo(ctx.packageName, 0).versionName }
                Text("Version $version · made for personal use · open source (MIT)",
                     style = MaterialTheme.typography.bodyMedium)
                OutlinedButton(onClick = {
                    try {
                        ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse("https://github.com/Husarp/linkpilot/releases/latest"))
                            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                    } catch (_: Exception) { Toast.makeText(ctx, "No browser to open GitHub in", Toast.LENGTH_SHORT).show() }
                }) { Text("Check for updates on GitHub") }
                Text("Opens the newest release in your browser - you have $version.", style = MaterialTheme.typography.bodySmall,
                     color = MaterialTheme.colorScheme.onSurfaceVariant)
                TextButton(onClick = showSetup) { Text("Show the setup again") }
            }
        }
    }
    if (adding) AddCategoryDialog(m) { adding = false }
}

@Composable
fun AddCategoryDialog(m: Model, close: () -> Unit) {
    m.tick   // coming back from Android's "allow connection" page shows the work profile's browsers
    val ctx = LocalContext.current
    val browsers = remember { Browsers.all(ctx) }
    val others = remember { Profiles.others(ctx) }
    var name by remember { mutableStateOf("") }
    var chosen by remember { mutableStateOf<Category?>(null) }   // its browser (and profile); the name is added on Add
    val taken = m.store.categories.any { it.name.equals(name.trim(), ignoreCase = true) }
    fun pick(c: Category, label: String) { chosen = c; if (name.isBlank()) name = label }

    AlertDialog(
        onDismissRequest = close,
        title = { Text("Add a category") },
        text = {
            Column(Modifier.heightIn(max = 480.dp).verticalScroll(rememberScrollState())) {
                OutlinedTextField(value = name, onValueChange = { name = it }, label = { Text("Name - Work, Home...") }, singleLine = true,
                                  isError = taken, supportingText = { if (taken) Text("There is one with this name already") })
                Text("Its browser", style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(top = 8.dp))
                if (browsers.isEmpty()) Text("No browsers found on this phone.")
                browsers.forEach { b ->
                    val c = Category("", b.pkg)
                    Row(Modifier.fillMaxWidth().clickable { pick(c, b.label) }, verticalAlignment = Alignment.CenterVertically) {
                        RadioButton(selected = chosen == c, onClick = { pick(c, b.label) })
                        AppIcon(b.pkg); Spacer(Modifier.width(8.dp)); Text(b.label)
                    }
                }
                others.forEach { user -> WorkProfileBrowsers(m, user, chosen) { c, label -> pick(c, label) } }
            }
        },
        confirmButton = {
            TextButton(enabled = chosen != null && name.isNotBlank() && !taken, onClick = {
                val store = m.store
                store.categories = store.categories + chosen!!.copy(name = name.trim())
                if (store.categories.size == 1) store.active = name.trim()
                m.changed(); close()
            }) { Text("Add") }
        },
        dismissButton = { TextButton(onClick = close) { Text("Cancel") } })
}

// The browsers in the work profile (Island's) - or what is still needed before they can be used.
@Composable
fun WorkProfileBrowsers(m: Model, user: android.os.UserHandle, chosen: Category?, pick: (Category, String) -> Unit) {
    m.tick
    val ctx = LocalContext.current
    val serial = remember(user) { Profiles.serial(ctx, user) }
    val state = Profiles.state(ctx, user)
    var everything by remember { mutableStateOf(false) }
    val small = MaterialTheme.typography.bodySmall
    Text("In the work profile", style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(top = 12.dp))
    when (state) {
        Profiles.State.TooOld -> Text("Needs Android 11 or newer.", style = small)
        Profiles.State.NotInstalled -> Text("Install LinkPilot in the work profile too (in Island: clone it). Then its " +
            "browsers show here.", style = small)
        Profiles.State.CanAsk -> {
            Text("LinkPilot needs Android's permission to reach its copy there.", style = small)
            TextButton(onClick = { Profiles.askIntent(ctx)?.let { ctx.startActivity(it.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) } }) {
                Text("Allow the connection")
            }
        }
        Profiles.State.NotAllowed -> Text("The work profile's app (Island) offers no switch for this. Allow it once from a " +
            "computer:\nadb shell appops set ${ctx.packageName} INTERACT_ACROSS_PROFILES allow", style = small)
        Profiles.State.Ready -> {
            val apps = remember(user) { Profiles.apps(ctx, user) }
            val here = remember { Browsers.all(ctx).map { it.pkg }.toSet() }
            val browsers = apps.filter { it.pkg in here || Profiles.isBrowser(ctx, it.pkg) }
            (if (everything) apps else browsers).forEach { a ->
                val c = Category("", a.pkg, serial)
                Row(Modifier.fillMaxWidth().clickable { pick(c, "${a.label} (work)") }, verticalAlignment = Alignment.CenterVertically) {
                    RadioButton(selected = chosen == c, onClick = { pick(c, "${a.label} (work)") })
                    val icon = remember(a.pkg) { Profiles.icon(ctx, a.pkg, user)?.toBitmap(96, 96)?.asImageBitmap() }
                    if (icon != null) Image(icon, null, Modifier.size(36.dp)) else Spacer(Modifier.size(36.dp))
                    Spacer(Modifier.width(8.dp)); Text(a.label)
                }
            }
            if (browsers.isEmpty() && !everything) Text("No browsers found there.", style = small)
            TextButton(onClick = { everything = !everything }) { Text(if (everything) "Show only browsers" else "Show all its apps") }
        }
    }
}

// ---- Shortcuts: Quick Settings tiles, home-screen widgets -----------------------------------------

@Composable
fun ShortcutsTab(m: Model) {
    m.tick
    val ctx = LocalContext.current
    var picking by remember { mutableStateOf<Tiles.Kind?>(null) }
    val noPin = "If nothing appears: long-press an empty spot on the home screen › Widgets › LinkPilot."

    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        item { PageTitle("Shortcuts", "Switch without opening the app") }
        item {
            SectionCard("Quick Settings tiles", "In the panel you pull down from the top", info = listOf(
                "Live category: Next browser and Choose browser show it.",
                "Long press: on any tile, opens LinkPilot.",
                "Add: asks Android to place the tile - \"Add tile / Do not add tile\" is Android's own question.",
                "By hand: pull down the panel › pencil › drag the tiles up.")) {
                for (kind in Tiles.kinds) {
                    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 4.dp)) {
                        Box(Modifier.size(44.dp).background(MaterialTheme.colorScheme.secondaryContainer, CircleShape),
                            contentAlignment = Alignment.Center) {
                            Icon(painterResource(Tiles.iconOf(ctx, kind)), null, Modifier.size(24.dp),
                                 tint = MaterialTheme.colorScheme.onSecondaryContainer)
                        }
                        Spacer(Modifier.width(12.dp))
                        Column(Modifier.weight(1f)) {
                            Text(ctx.getString(kind.label), style = MaterialTheme.typography.titleMedium)
                            Text(when (kind) {
                                     Tiles.next -> "Each tap: the next category"
                                     Tiles.choose -> "A tap: the list to choose from"
                                     else -> "A tap: cleans the link you copied"
                                 }, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(start = 56.dp)) {
                        if (Build.VERSION.SDK_INT >= 33) OutlinedButton(onClick = { addTile(ctx, kind) }) { Text("Add") }
                        OutlinedButton(onClick = { picking = kind }) { Text("Change icon") }
                    }
                }
            }
        }
        item {
            SectionCard("Home-screen widgets", "They show the live category and its browser's icon") {
                for ((next, name, line) in listOf(Triple(true, R.string.tile_next, "Each tap: the next category"),
                                                  Triple(false, R.string.tile_choose, "A tap: the list to choose from"))) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text(ctx.getString(name), style = MaterialTheme.typography.titleMedium)
                            Text(line, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        OutlinedButton(onClick = { if (!Widgets.pin(ctx, next)) Toast.makeText(ctx, noPin, Toast.LENGTH_LONG).show() }) {
                            Text("Add")
                        }
                    }
                }
                Text("Or: long-press the home screen › Widgets › LinkPilot", style = MaterialTheme.typography.bodySmall,
                     color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
    picking?.let { kind -> IconPicker(m, kind) { picking = null } }
}

// A choice of simple shapes for a tile - Android draws tile icons in one colour, so a browser's
// own coloured icon would come out as a plain blob.
@Composable
fun IconPicker(m: Model, kind: Tiles.Kind, close: () -> Unit) {
    val ctx = LocalContext.current
    val now = m.store.tileIcon(kind.key) ?: kind.defaultIcon
    AlertDialog(
        onDismissRequest = close,
        title = { Text("Icon for \"${ctx.getString(kind.label)}\"") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Tiles.icons.chunked(4).forEach { row ->
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        row.forEach { (name, res) ->
                            Box(Modifier.size(56.dp)
                                    .background(if (name == now) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceVariant,
                                                CircleShape)
                                    .clickable { m.store.setTileIcon(kind.key, name); m.changed(); close() },
                                contentAlignment = Alignment.Center) {
                                Icon(painterResource(res), name, Modifier.size(28.dp))
                            }
                        }
                    }
                }
            }
        },
        confirmButton = { TextButton(onClick = close) { Text("Cancel") } })
}

@RequiresApi(33)
fun addTile(ctx: Context, kind: Tiles.Kind) {
    ctx.getSystemService(StatusBarManager::class.java).requestAddTileService(
        ComponentName(ctx, kind.service), ctx.getString(kind.label),
        android.graphics.drawable.Icon.createWithResource(ctx, Tiles.iconOf(ctx, kind)), ctx.mainExecutor) { }
}

// ---- Rules -------------------------------------------------------------------------------------------

@Composable
fun RulesTab(m: Model) {
    m.tick
    val store = m.store
    val rules = store.rules
    val cats = store.categories
    var adding by remember { mutableStateOf<Boolean?>(null) }   // true: an app rule, false: an address rule

    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        item { PageTitle("Link rules", "Send some links to their own category") }
        item {
            SectionCard("Rules", "From an app, or to an address", info = listOf(
                "Order: the first rule that matches decides - rules win over the live category.",
                "Use rules off: every link goes to the live category; your rules are kept.",
                "App rules: work when Android says which app sent the link - most apps do. For the rest, use an address rule.")) {
                SwitchRow("Use rules", "Off: every link goes to the live category", store.rulesOn) { store.rulesOn = it; m.changed() }
                HorizontalDivider()
                if (rules.isEmpty()) Text("No rules yet.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                rules.forEachIndexed { i, r ->
                    val gone = cats.none { it.name.equals(r.category, ignoreCase = true) }
                    ListItem(
                        colors = cardItem,
                        leadingContent = { Switch(checked = r.on, onCheckedChange = { on ->
                            store.rules = rules.toMutableList().also { it[i] = r.copy(on = on) }; m.changed() }) },
                        headlineContent = { Text(if (r.byApp) "From ${r.shown}" else r.shown) },
                        supportingContent = { Text("→ ${r.category}" + if (gone) "  (category gone - skipped)" else "") },
                        trailingContent = {
                            IconButton(onClick = { store.rules = rules.filterIndexed { j, _ -> j != i }; m.changed() }) {
                                Icon(Icons.Default.Delete, "Remove")
                            }
                        })
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = { adding = true }, enabled = cats.isNotEmpty()) { Text("+ App rule") }
                    OutlinedButton(onClick = { adding = false }, enabled = cats.isNotEmpty()) { Text("+ Address rule") }
                }
            }
        }
    }
    adding?.let { byApp -> AddRuleDialog(m, byApp) { adding = null } }
}

@Composable
fun AddRuleDialog(m: Model, byApp: Boolean, close: () -> Unit) {
    val ctx = LocalContext.current
    val store = m.store
    val names = store.categories.map { it.name }
    var category by remember { mutableStateOf(names.firstOrNull()) }
    var address by remember { mutableStateOf("") }
    var app by remember { mutableStateOf<Browsers.App?>(null) }
    var search by remember { mutableStateOf("") }
    val recent = remember { store.recentApps.map { Browsers.App(it, Browsers.label(ctx, it)) } }
    val apps = remember { Browsers.apps(ctx) }

    AlertDialog(
        onDismissRequest = close,
        title = { Text(if (byApp) "Links from an app" else "Links to an address") },
        text = {
            Column(Modifier.heightIn(max = 520.dp).verticalScroll(rememberScrollState())) {
                if (byApp) {
                    OutlinedTextField(value = search, onValueChange = { search = it }, label = { Text("Find an app") }, singleLine = true)
                    val shown = (if (search.isBlank()) recent else emptyList()) +
                                apps.filter { a -> search.isBlank() || a.label.contains(search.trim(), ignoreCase = true) }
                                    .filter { a -> search.isNotBlank() || recent.none { it.pkg == a.pkg } }
                    if (recent.isNotEmpty() && search.isBlank()) Text("Opened links lately first", style = MaterialTheme.typography.bodySmall)
                    shown.take(80).forEach { a ->
                        Row(Modifier.fillMaxWidth().clickable { app = a }, verticalAlignment = Alignment.CenterVertically) {
                            RadioButton(selected = app?.pkg == a.pkg, onClick = { app = a })
                            AppIcon(a.pkg); Spacer(Modifier.width(8.dp)); Text(a.label)
                        }
                    }
                } else {
                    OutlinedTextField(value = address, onValueChange = { address = it }, singleLine = true,
                                      label = { Text("Address - github.com") },
                                      supportingText = { Text("Also every site under it. With a / it is looked for anywhere in the link.") })
                }
                HorizontalDivider(Modifier.padding(vertical = 8.dp))
                Text("Open them in", style = MaterialTheme.typography.titleMedium)
                CategoryPicker(names, category) { category = it }
            }
        },
        confirmButton = {
            TextButton(enabled = category != null && (if (byApp) app != null else Router.cleanAddress(address).isNotEmpty()), onClick = {
                val rule = if (byApp) Rule(true, true, app!!.label, app!!.pkg, category!!)
                           else Rule(true, false, Router.cleanAddress(address), Router.cleanAddress(address).lowercase(), category!!)
                store.rules = store.rules + rule
                m.changed(); close()
            }) { Text("Add") }
        },
        dismissButton = { TextButton(onClick = close) { Text("Cancel") } })
}

// ---- Cleaning: opened links, copied links, try a link, what gets removed -------------------------

@Composable
fun CleaningTab(m: Model) {
    m.tick
    val store = m.store
    val options = store.cleanOptions()
    var tryLink by rememberSaveable {
        mutableStateOf("https://www.google.com/url?q=https://www.youtube.com/watch%3Fv%3DdQw4w9WgXcQ%26si%3DxYz123&sa=D&utm_source=chat")
    }
    val tried = Cleaner.apply(tryLink.trim(), options)

    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        item { PageTitle("Link cleaning", "Tracking taken out of links") }
        item {
            SectionCard("Opened links", "Links you tap, before they open", info = listOf(
                "Tracking: parts that say where a click came from - utm_source, fbclid, YouTube's si, shops' own. The page " +
                    "is the same without them.",
                "Redirects: middlemen such as google.com/url?q=... that learn where you go. The real link inside opens directly.",
                "Safe: only known parts are removed, so links keep working. A short message says what was taken out.")) {
                SwitchRow("Remove tracking", "utm_source, fbclid, si, shop tracking...", store.cleanOn) { store.cleanOn = it; m.changed() }
                SwitchRow("Skip redirects", "google.com/url?q=... and 14 more", store.unwrapOn) { store.unwrapOn = it; m.changed() }
            }
        }
        item { CopiedLinksCard(m) }
        item {
            SectionCard("Try a link", "See what a link becomes") {
                OutlinedTextField(value = tryLink, onValueChange = { tryLink = it }, label = { Text("A link") }, modifier = Modifier.fillMaxWidth())
                Text(tried.url, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold)
                Text(if (tryLink.isBlank()) "" else tried.changes.ifEmpty { "nothing to change" },
                     style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
        item { WhatGetsRemovedCard(m, options) }
    }
}

// Copied links: cleaned by themselves (CopyWatch.kt) if that is on - it needs LinkPilot allowed
// in Android's Accessibility settings too - and always on a tap.
@Composable
fun CopiedLinksCard(m: Model) {
    m.tick   // coming back from Android's settings shows whether the service is on now
    val ctx = LocalContext.current
    val store = m.store
    SectionCard("Copied links", "Links you copy, before you paste", info = listOf(
        "Why Accessibility: Android lets an app read what you copied only while it is on screen.",
        "What it hears: only the system's own \"copied\" preview - never other apps. LinkPilot cannot go online.",
        "What changes: only a link on its own. What a password manager copies is left alone.",
        "Android 10-12: they give no sign of a copy - use the button or the tile there.",
        "Also on a tap: the \"Clean copied link\" tile (Shortcuts tab), or Share › Copy clean link.")) {
        if (CopyWatchService.possible) {
            SwitchRow("Clean automatically", "As soon as you copy a link", store.cleanCopiedAuto) { store.cleanCopiedAuto = it; m.changed() }
            if (store.cleanCopiedAuto) {
                if (CopyWatchService.enabled(ctx)) Text("✓  On - copied links are cleaned by themselves",
                                                       color = MaterialTheme.colorScheme.primary, fontWeight = FontWeight.SemiBold)
                else AccessibilityStepsCard()
            }
        } else {
            Text("Not possible on Android 10-12 - use the button below, or the tile.", style = MaterialTheme.typography.bodyMedium)
        }
        OutlinedButton(onClick = { Toast.makeText(ctx, Clipboard.cleanNow(ctx) ?: "", Toast.LENGTH_LONG).show(); m.changed() }) {
            Text("Clean the copied link now")
        }
    }
}

// What to do in Android's Accessibility settings - one step per line.
@Composable
fun AccessibilityStepsCard() {
    val ctx = LocalContext.current
    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.tertiaryContainer)) {
        Column(Modifier.fillMaxWidth().padding(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Text("One more step: allow it in Accessibility", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold,
                 color = MaterialTheme.colorScheme.onTertiaryContainer)
            NumberedSteps(listOf(
                "Tap Open Accessibility settings below",
                "Choose Downloaded apps (or Installed apps)",
                "Tap LinkPilot and switch it on",
                "Confirm with Allow"))
            Button(onClick = { openAccessibility(ctx) }, modifier = Modifier.fillMaxWidth()) { Text("Open Accessibility settings") }
            Text("If Android says the setting is restricted: Settings › Apps › LinkPilot › ⋮ (top right) › Allow restricted " +
                 "settings - then try again.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onTertiaryContainer)
        }
    }
}

// Straight to LinkPilot's own switch where Android allows it, otherwise the Accessibility page.
fun openAccessibility(ctx: Context) {
    val own = Intent("android.settings.ACCESSIBILITY_DETAILS_SETTINGS")
        .putExtra(Intent.EXTRA_COMPONENT_NAME, ComponentName(ctx, CopyWatchService::class.java).flattenToString())
    try { ctx.startActivity(own.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
    catch (_: Exception) { ctx.startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
}

// The tracking parts, grouped by the site they are for, and the redirects - each group folded away.
@Composable
fun WhatGetsRemovedCard(m: Model, options: Cleaner.Options) {
    val store = m.store
    val groups = remember { Cleaner.parts.groupBy { siteGroup(it) } }
    SectionCard("What gets removed", "${Cleaner.parts.size} tracking parts · ${Cleaner.redirects.size} redirects · untick one to keep it") {
        groups.forEach { (name, parts) ->
            Group(name, "${parts.count { options.isOn(it) }} of ${parts.size}") {
                parts.forEach { p ->
                    TickRow(p.name, p.what, options.isOn(p)) { on ->
                        store.partsOff = if (on) store.partsOff - p.id else store.partsOff + p.id; m.changed()
                    }
                }
            }
        }
        Group("Redirects skipped", "${Cleaner.redirects.count { options.isOn(it) }} of ${Cleaner.redirects.size}") {
            Cleaner.redirects.forEach { r ->
                TickRow(r.host.replace(".*", "") + r.path.trimEnd('/'), r.what, options.isOn(r)) { on ->
                    store.redirectsOff = if (on) store.redirectsOff - r.id else store.redirectsOff + r.id; m.changed()
                }
            }
        }
    }
}

private fun siteGroup(p: Cleaner.Part): String {
    if (p.sites.isEmpty()) return "Every site"
    return when (val first = p.sites.split(' ')[0]) {
        "youtube.com" -> "YouTube and Spotify"
        "twitter.com" -> "X / Twitter"
        "amazon.*" -> "Amazon"
        "ebay.*" -> "eBay"
        "aliexpress.*" -> "AliExpress"
        "allegro.*" -> "Allegro"
        "temu.*" -> "Temu"
        "shein.*" -> "Shein"
        "etsy.com" -> "Etsy"
        "walmart.*" -> "Walmart"
        "ceneo.pl" -> "Ceneo"
        else -> first
    }
}

// A folded group: its name and how many are on; a tap opens it.
@Composable
fun Group(name: String, count: String, content: @Composable () -> Unit) {
    var open by rememberSaveable(name) { mutableStateOf(false) }
    Column {
        Row(Modifier.fillMaxWidth().clickable { open = !open }.padding(vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(name, style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
            Text(count, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Icon(if (open) Icons.Default.KeyboardArrowUp else Icons.Default.KeyboardArrowDown, if (open) "Close" else "Open")
        }
        if (open) content()
        HorizontalDivider()
    }
}

@Composable
fun TickRow(title: String, text: String, on: Boolean, change: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().clickable { change(!on) }, verticalAlignment = Alignment.CenterVertically) {
        Checkbox(checked = on, onCheckedChange = change)
        Column {
            Text(title, fontWeight = FontWeight.SemiBold)
            Text(text, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

// ---- Log -------------------------------------------------------------------------------------------

@Composable
fun LogTab(m: Model) {
    m.tick
    val ctx = LocalContext.current
    val store = m.store
    val entries = m.log ?: emptyList()   // read by the Model, away from the screen
    var open by rememberSaveable { mutableStateOf<String?>(null) }   // the link shown in full, by its place in the log
    var clearing by remember { mutableStateOf(false) }
    val days = remember(entries) { entries.withIndex().groupBy { it.value.time.take(10) } }   // newest first, as read

    // Only the rows on screen are drawn, and names and icons were looked up by the Model beforehand,
    // so the newest 300 scroll as smoothly as a few.
    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        item { PageTitle("Link log", "Every link, and what happened to it") }
        item {
            SectionCard("Keep a log", "On this phone only, the newest 300 · tap a link for more") {
                SwitchRow("Keep a log of links",
                          when { m.log == null -> "Reading..."; entries.isEmpty() -> "No links yet"; else -> "${entries.size} links" },
                          store.logOn) { store.logOn = it; m.changed() }
                TextButton(onClick = { clearing = true }, enabled = entries.isNotEmpty()) { Text("Clear log") }
            }
        }
        days.forEach { (day, list) ->
            item(key = "day $day") {
                Text(dayName(day), style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary,
                     modifier = Modifier.padding(top = 8.dp, start = 4.dp))
            }
            items(list, key = { "link ${it.index}" }) { (index, e) ->
                val id = "$index ${e.time} ${e.opened}"
                LogRow(m, e, open == id) { open = if (open == id) null else id }
            }
        }
    }
    if (clearing) AlertDialog(
        onDismissRequest = { clearing = false },
        title = { Text("Clear the whole log?") },
        text = { Text("All ${entries.size} links are deleted from this phone.") },
        confirmButton = { TextButton(onClick = { store.clearLog(); clearing = false; m.changed() }) { Text("Clear") } },
        dismissButton = { TextButton(onClick = { clearing = false }) { Text("Cancel") } })
}

// "Today", "Yesterday", or "Wed 23 Sep".
private fun dayName(day: String): String {
    val f = SimpleDateFormat("yyyy-MM-dd", Locale.ROOT)
    val today = f.format(Date())
    val yesterday = f.format(Date(System.currentTimeMillis() - 24L * 3600 * 1000))
    return when (day) {
        today -> "Today"
        yesterday -> "Yesterday"
        else -> try { SimpleDateFormat("EEE d MMM", Locale.getDefault()).format(f.parse(day)!!) } catch (_: Exception) { day }
    }
}

// One link: the app it came from, the site, the time, where it went - and, opened, all of it.
@Composable
fun LogRow(m: Model, e: LogEntry, expanded: Boolean, toggle: () -> Unit) {
    val ctx = LocalContext.current
    val copied = e.openedIn == "(copied)"
    val site = remember(e.opened) { Cleaner.hostOf(e.opened)?.removePrefix("www.") ?: e.opened }
    val rest = remember(e.opened) { e.opened.substringAfter("://").substringAfter('/', "").let { if (it.isEmpty()) "" else "/$it" } }
    val from = if (e.from.isEmpty()) "an unknown app" else m.appLabel(e.from)
    val icon = remember(e.from) { if (e.from.isEmpty()) null else m.appIcon(e.from) }
    Card(Modifier.fillMaxWidth().clickable(onClick = toggle),
         colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerLow)) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                if (icon != null) Image(icon, null, Modifier.size(32.dp))
                else Box(Modifier.size(32.dp).background(MaterialTheme.colorScheme.secondaryContainer, CircleShape), contentAlignment = Alignment.Center) {
                    Icon(painterResource(R.drawable.ic_t_link), null, Modifier.size(18.dp), tint = MaterialTheme.colorScheme.onSecondaryContainer)
                }
                Spacer(Modifier.width(12.dp))
                Column(Modifier.weight(1f)) {
                    Text(site, style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    if (rest.isNotEmpty()) Text(rest, style = MaterialTheme.typography.bodySmall, maxLines = 1, overflow = TextOverflow.Ellipsis,
                                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                Text(e.time.drop(11), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
                Tag(if (copied) "Copied" else "→ ${e.openedIn}", MaterialTheme.colorScheme.secondaryContainer, MaterialTheme.colorScheme.onSecondaryContainer)
                if (e.changes.isNotEmpty()) Tag("Cleaned", MaterialTheme.colorScheme.tertiaryContainer, MaterialTheme.colorScheme.onTertiaryContainer)
                Text("from $from", style = MaterialTheme.typography.bodySmall, maxLines = 1, overflow = TextOverflow.Ellipsis,
                     color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            if (expanded) {
                HorizontalDivider(Modifier.padding(vertical = 4.dp))
                Detail("Link", e.opened)
                if (e.asked != e.opened) Detail("It came as", e.asked)
                if (e.changes.isNotEmpty()) Detail("Taken out", e.changes.removePrefix("removed "))
                Detail(if (copied) "What happened" else "Why there", e.why)
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = {
                        ctx.getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText("link", e.opened))
                        Toast.makeText(ctx, "Link copied", Toast.LENGTH_SHORT).show()
                    }) { Text("Copy link") }
                    OutlinedButton(onClick = {   // through LinkPilot again: the rules and the live category decide
                        try { ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(e.opened)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
                        catch (_: Exception) { }
                    }) { Text("Open again") }
                }
            }
        }
    }
}

@Composable
private fun Tag(text: String, back: androidx.compose.ui.graphics.Color, ink: androidx.compose.ui.graphics.Color) {
    Text(text, style = MaterialTheme.typography.labelMedium, color = ink, maxLines = 1,
         modifier = Modifier.background(back, RoundedCornerShape(8.dp)).padding(horizontal = 8.dp, vertical = 3.dp))
}

@Composable
private fun Detail(label: String, text: String) {
    Column {
        Text(label, style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.primary)
        Text(text, style = MaterialTheme.typography.bodyMedium)
    }
}
