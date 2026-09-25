package com.husarp.linkpilot

import android.annotation.SuppressLint
import android.app.Activity
import android.app.PendingIntent
import android.content.ClipData
import android.content.ClipDescription
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.os.Build
import android.widget.Toast

// Cleaning a copied link. Since Android 10 an app may read what was copied only while it is on screen
// (or is the keyboard), so this cannot watch the clipboard by itself as the Windows app does: it
// happens on a tap - the "Clean copied link" tile, or the button on the Cleaning tab. The tile opens
// an invisible screen for a moment, which is what lets it read.
//
// As on Windows, only a copied link on its own is changed, and what a password manager marks as
// sensitive is not looked at. What was copied is never kept - only the link in the link log, if on.
object Clipboard {
    // Cleans what is copied now; the message to show. quiet (when it happens by itself, CopyWatch.kt):
    // a message only if a link was cleaned - nothing is said about everything else that gets copied.
    fun cleanNow(ctx: Context, quiet: Boolean = false): String? {
        val cm = ctx.getSystemService(ClipboardManager::class.java)
        val clip = cm.primaryClip ?: return if (quiet) null else "Nothing is copied"
        if (Build.VERSION.SDK_INT >= 33 && clip.description.extras?.getBoolean(ClipDescription.EXTRA_IS_SENSITIVE) == true)
            return if (quiet) null else "What is copied is marked private - left alone"
        val text = if (clip.itemCount > 0) clip.getItemAt(0).coerceToText(ctx).toString() else ""
        val link = text.trim()
        if (link.isEmpty() || link.any { it.isWhitespace() } || !Cleaner.isWebLink(link))
            return if (quiet) null else "What is copied is not a link on its own - left as it is"
        return put(ctx, text, auto = quiet) ?: if (quiet) null else "The copied link is clean already"
    }

    // Cleans this copied text - a link on its own - and puts the clean link on the clipboard in its
    // place (writing is always allowed; only reading is not). The message, or null if nothing to clean.
    fun put(ctx: Context, text: String, auto: Boolean): String? {
        val link = text.trim()
        val store = Store(ctx)
        val cleaned = Cleaner.apply(link, store.cleanOptions())
        if (cleaned.changes.isEmpty()) return null
        ctx.getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText("link", text.replace(link, cleaned.url)))
        store.addLog("", "(copied)", if (auto) "the clipboard - cleaned when copied" else "the clipboard - cleaned on a tap",
                     link, cleaned.url, cleaned.changes)
        return "Copied link cleaned - ${cleaned.changes}"
    }
}

// The moment on screen the tile needs: reads once it has the focus (not before - Android would say
// nothing is copied), cleans, says so, and is gone.
class CleanClipboardActivity : Activity() {
    private var done = false

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (!hasFocus || done) return
        done = true
        Toast.makeText(applicationContext, Clipboard.cleanNow(this) ?: "", Toast.LENGTH_LONG).show()
        finish()
    }
}

class CleanTile : WatchingTile(Tiles.clean) {
    @SuppressLint("StartActivityAndCollapseDeprecated")   // the Intent form only before Android 14, where it is the only one
    override fun onClick() {
        val intent = Intent(this, CleanClipboardActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        if (Build.VERSION.SDK_INT >= 34)
            startActivityAndCollapse(PendingIntent.getActivity(this, 3, intent, PendingIntent.FLAG_IMMUTABLE))
        else
            @Suppress("DEPRECATION") startActivityAndCollapse(intent)
    }
}
