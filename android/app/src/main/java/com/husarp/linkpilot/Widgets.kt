package com.husarp.linkpilot

import android.app.Activity
import android.app.AlertDialog
import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.widget.RemoteViews
import android.widget.Toast
import androidx.core.graphics.drawable.toBitmap

// Home-screen widgets - the same two as the Quick Settings tiles, for the home screen: "Next
// browser" (each tap makes the next category live) and "Choose browser" (a tap shows the list).
// Both show the live category with its browser's own icon.
object Widgets {
    private val kinds = listOf(NextWidget::class.java, ChooseWidget::class.java)

    // Redraws every LinkPilot widget on the home screen - after the live category changed anywhere.
    fun update(ctx: Context) {
        val manager = AppWidgetManager.getInstance(ctx)
        for (k in kinds) {
            val ids = manager.getAppWidgetIds(ComponentName(ctx, k))
            if (ids.isNotEmpty()) draw(ctx, manager, ids, k == NextWidget::class.java)
        }
    }

    fun draw(ctx: Context, manager: AppWidgetManager, ids: IntArray, next: Boolean) {
        val live = Store(ctx).live()
        val views = RemoteViews(ctx.packageName, R.layout.widget)
        views.setTextViewText(R.id.widget_name, live?.name ?: "No categories yet")
        views.setTextViewText(R.id.widget_hint, if (next) "Tap: next browser" else "Tap: choose")
        val icon = live?.let { c -> Browsers.icon(ctx, c)?.toBitmap(96, 96) }
        if (icon != null) views.setImageViewBitmap(R.id.widget_icon, icon) else views.setImageViewResource(R.id.widget_icon, R.mipmap.ic_launcher)
        val tap = if (next)
            PendingIntent.getBroadcast(ctx, 1, Intent(ctx, NextWidget::class.java).setAction(NextWidget.STEP), PendingIntent.FLAG_IMMUTABLE)
        else
            PendingIntent.getActivity(ctx, 2, Intent(ctx, ChooseActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
                                      PendingIntent.FLAG_IMMUTABLE)
        views.setOnClickPendingIntent(R.id.widget_root, tap)
        manager.updateAppWidget(ids, views)
    }

    // Asks the home screen to place one (Android 8+, if the home screen supports it).
    fun pin(ctx: Context, next: Boolean): Boolean {
        val manager = AppWidgetManager.getInstance(ctx)
        if (!manager.isRequestPinAppWidgetSupported) return false
        return manager.requestPinAppWidget(ComponentName(ctx, if (next) NextWidget::class.java else ChooseWidget::class.java), null, null)
    }
}

class NextWidget : AppWidgetProvider() {
    companion object { const val STEP = "com.husarp.linkpilot.STEP" }

    override fun onUpdate(ctx: Context, manager: AppWidgetManager, ids: IntArray) = Widgets.draw(ctx, manager, ids, true)

    override fun onReceive(ctx: Context, intent: Intent) {
        super.onReceive(ctx, intent)
        if (intent.action != STEP) return
        val c = Store(ctx).step(1) ?: return
        Tiles.refresh(ctx)
        Toast.makeText(ctx, "Links now open in ${c.name}", Toast.LENGTH_SHORT).show()
    }
}

class ChooseWidget : AppWidgetProvider() {
    override fun onUpdate(ctx: Context, manager: AppWidgetManager, ids: IntArray) = Widgets.draw(ctx, manager, ids, false)
}

// The list the "Choose browser" widget shows - over the home screen, nothing else opens.
class ChooseActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val store = Store(this)
        val cats = store.categories
        if (cats.isEmpty()) { finish(); return }
        val names = cats.map { "${it.name}  -  ${Browsers.label(this, it)}" }.toTypedArray()
        AlertDialog.Builder(this, android.R.style.Theme_DeviceDefault_Dialog_Alert)
            .setTitle("Links open in")
            .setSingleChoiceItems(names, cats.indexOfFirst { it.name == store.live()?.name }) { d, i ->
                store.active = cats[i].name
                Tiles.refresh(this)
                d.dismiss()
            }
            .setNegativeButton("Cancel", null)
            .setOnDismissListener { finish() }
            .show()
    }
}
