package com.husarp.linkpilot

import android.app.AlertDialog
import android.content.ComponentName
import android.content.Context
import android.content.SharedPreferences
import android.graphics.drawable.Icon
import android.os.Build
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import android.widget.Toast

// The two Quick Settings tiles - the buttons in the panel pulled down from the top of the screen.
// Both show the live category. "Next browser": each tap makes the next category live. "Choose
// browser": a tap shows the list. A long press on either opens LinkPilot. Each has an icon
// of your choice (Android draws tile icons in one colour, so these are simple shapes).
object Tiles {
    class Kind(val key: String, val service: Class<*>, val label: Int, val hint: String, val defaultIcon: String)

    val next = Kind("next", NextTile::class.java, R.string.tile_next, "Tap: next", "swap")
    val choose = Kind("choose", ChooseTile::class.java, R.string.tile_choose, "Tap: choose", "list")
    val clean = Kind("clean", CleanTile::class.java, R.string.tile_clean, "Tap: clean it", "link")   // Clipboard.kt
    val kinds = listOf(next, choose, clean)

    // The icons to choose from, by the name kept in the settings.
    val icons = listOf(
        "swap" to R.drawable.ic_tile, "next" to R.drawable.ic_t_next, "list" to R.drawable.ic_t_list,
        "globe" to R.drawable.ic_t_globe, "work" to R.drawable.ic_t_work, "home" to R.drawable.ic_t_home,
        "school" to R.drawable.ic_t_school, "person" to R.drawable.ic_t_person, "shield" to R.drawable.ic_t_shield,
        "star" to R.drawable.ic_t_star, "heart" to R.drawable.ic_t_heart, "link" to R.drawable.ic_t_link,
    )

    fun iconOf(ctx: Context, kind: Kind): Int {
        val name = Store(ctx).tileIcon(kind.key) ?: kind.defaultIcon
        return icons.firstOrNull { it.first == name }?.second ?: R.drawable.ic_tile
    }

    fun paint(service: TileService, kind: Kind) {
        val tile = service.qsTile ?: return
        if (kind == clean) {   // not about categories: always the same
            tile.label = service.getString(kind.label)
            if (Build.VERSION.SDK_INT >= 29) tile.subtitle = kind.hint
            tile.icon = Icon.createWithResource(service, iconOf(service, kind))
            tile.state = Tile.STATE_INACTIVE
            tile.updateTile()
            return
        }
        val live = Store(service).live()
        tile.label = live?.name ?: service.getString(R.string.app_name)
        if (Build.VERSION.SDK_INT >= 29) tile.subtitle = if (live == null) "No categories yet" else kind.hint
        tile.icon = Icon.createWithResource(service, iconOf(service, kind))
        tile.state = if (live == null) Tile.STATE_UNAVAILABLE else Tile.STATE_ACTIVE
        tile.updateTile()
    }

    // After the live category (or an icon) changed anywhere: the tiles show it the next time they are
    // seen, and the home-screen widgets at once.
    fun refresh(ctx: Context) {
        for (k in kinds)
            try { TileService.requestListeningState(ctx, ComponentName(ctx, k.service)) } catch (_: Exception) { }
        Widgets.update(ctx)
    }
}

// While the panel is open, a tile repaints itself whenever a setting changes - the live category
// from the other tile or the app, or its icon - so the two never show different categories.
abstract class WatchingTile(private val kind: Tiles.Kind) : TileService() {
    private val changed = SharedPreferences.OnSharedPreferenceChangeListener { _, _ -> Tiles.paint(this, kind) }   // kept: Android holds it weakly

    override fun onStartListening() {
        Store.prefs(this).registerOnSharedPreferenceChangeListener(changed)
        Tiles.paint(this, kind)
    }

    override fun onStopListening() = Store.prefs(this).unregisterOnSharedPreferenceChangeListener(changed)
}

class NextTile : WatchingTile(Tiles.next) {
    override fun onClick() {
        val c = Store(this).step(1) ?: return
        Tiles.refresh(this)
        Toast.makeText(this, "Links now open in ${c.name}", Toast.LENGTH_SHORT).show()
    }
}

class ChooseTile : WatchingTile(Tiles.choose) {
    override fun onClick() {
        val store = Store(this)
        val cats = store.categories
        if (cats.isEmpty()) return
        val names = cats.map { "${it.name}  -  ${Browsers.label(this, it)}" }.toTypedArray()
        val now = cats.indexOfFirst { it.name == store.live()?.name }
        val dialog = AlertDialog.Builder(this, android.R.style.Theme_DeviceDefault_Dialog_Alert)
            .setTitle("Links open in")
            .setSingleChoiceItems(names, now) { d, i ->
                store.active = cats[i].name
                d.dismiss()
                Tiles.refresh(this)
            }
            .setNegativeButton("Cancel", null)
            .create()
        showDialog(dialog)
    }
}
