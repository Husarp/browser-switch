package com.husarp.linkpilot

import android.accessibilityservice.AccessibilityService
import android.content.ClipboardManager
import android.content.Context
import android.graphics.Color
import android.graphics.PixelFormat
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.view.Gravity
import android.view.View
import android.view.WindowManager
import android.view.accessibility.AccessibilityEvent
import android.widget.TextView
import android.widget.Toast

// Copied links cleaned by themselves - the Android side of Windows' "Clean copied links too".
//
// Since Android 10 an app may read what was copied only while it is on screen. So this is an
// accessibility service that listens to one app only - the system's own screen parts
// (com.android.systemui), set in res/xml/copy_watch.xml - never to other apps:
//
//   Android 13+   every copy makes the system show its small "copied" preview, and the preview says
//                 what was copied. A link on its own with tracking in it is cleaned and put back
//                 straight away. A very long one (the preview may cut it short) is read properly: a
//                 window nobody sees takes the focus for a moment, which is what lets it read.
//   Android 8-9   apps may still read in the background: a plain listener does it.
//   Android 10-12 no sign to go by: the tile or the button, as before.
//
// It does something only while "Clean copied links automatically" is on. Putting the clean link back
// shows the preview again - with nothing left to clean, so it stops there.
class CopyWatchService : AccessibilityService() {
    private val main = Handler(Looper.getMainLooper())
    private var busy = false

    private val listener = ClipboardManager.OnPrimaryClipChangedListener {
        if (Build.VERSION.SDK_INT < 29 && Store(this).cleanCopiedAuto) say(Clipboard.cleanNow(this, quiet = true))
    }

    override fun onServiceConnected() {
        getSystemService(ClipboardManager::class.java).addPrimaryClipChangedListener(listener)
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent) {
        if (Build.VERSION.SDK_INT < 33 || event.eventType != AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED) return
        val preview = event.text.singleOrNull()?.toString()?.trim() ?: return
        if (preview.isEmpty() || preview.any { it.isWhitespace() } || !Cleaner.isWebLink(preview)) return   // not a copied link
        val store = Store(this)
        if (!store.cleanCopiedAuto || Cleaner.apply(preview, store.cleanOptions()).changes.isEmpty()) return
        if (preview.length < 400 && !preview.endsWith("…")) say(Clipboard.put(this, preview, auto = true))
        else readWithFocus()
    }

    // Takes the focus for a moment with a window nobody sees, reads and cleans, and gives it back.
    private fun readWithFocus() {
        if (busy) return
        busy = true
        val wm = getSystemService(WindowManager::class.java)
        val view = object : View(this) {
            override fun onWindowFocusChanged(hasWindowFocus: Boolean) {
                super.onWindowFocusChanged(hasWindowFocus)
                if (!hasWindowFocus || !busy) return
                say(Clipboard.cleanNow(this@CopyWatchService, quiet = true))
                done(this)
            }
        }
        val params = WindowManager.LayoutParams(1, 1, WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL or WindowManager.LayoutParams.FLAG_WATCH_OUTSIDE_TOUCH,
            PixelFormat.TRANSPARENT)
        try { wm.addView(view, params) } catch (_: Exception) { busy = false; return }
        main.postDelayed({ if (busy) done(view) }, 1500)   // never keep the focus if it did not come
    }

    private fun done(view: View) {
        busy = false
        try { getSystemService(WindowManager::class.java).removeView(view) } catch (_: Exception) { }
    }

    // A message like a toast, drawn by the service itself: Android does not show an app's toasts while
    // it is in the background if its notifications are not allowed - and this always runs in the
    // background. It takes no touch and no focus, and goes after 2.5 seconds.
    private fun say(message: String?) {
        if (message == null) return
        val dp = resources.displayMetrics.density
        val bubble = TextView(this).apply {
            text = message
            setTextColor(Color.WHITE)
            textSize = 14f
            setPadding((18 * dp).toInt(), (12 * dp).toInt(), (18 * dp).toInt(), (12 * dp).toInt())
            background = GradientDrawable().apply { cornerRadius = 24 * dp; setColor(0xEE303034.toInt()) }
        }
        val params = WindowManager.LayoutParams(WindowManager.LayoutParams.WRAP_CONTENT, WindowManager.LayoutParams.WRAP_CONTENT,
            WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE,
            PixelFormat.TRANSLUCENT).apply {
            gravity = Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL
            y = (96 * dp).toInt()
        }
        val wm = getSystemService(WindowManager::class.java)
        try { wm.addView(bubble, params) } catch (_: Exception) { Toast.makeText(this, message, Toast.LENGTH_SHORT).show(); return }
        main.postDelayed({ try { wm.removeView(bubble) } catch (_: Exception) { } }, 2500)
    }

    override fun onInterrupt() {}

    override fun onDestroy() {
        try { getSystemService(ClipboardManager::class.java).removePrimaryClipChangedListener(listener) } catch (_: Exception) { }
        super.onDestroy()
    }

    companion object {
        // Whether Android has it switched on (Settings -> Accessibility -> LinkPilot).
        fun enabled(ctx: Context): Boolean {
            val on = android.provider.Settings.Secure.getString(ctx.contentResolver, android.provider.Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES) ?: return false
            return on.split(':').any { it.startsWith(ctx.packageName + "/") }
        }

        // Automatic cleaning works on Android 8-9 and 13+ (see above).
        val possible get() = Build.VERSION.SDK_INT < 29 || Build.VERSION.SDK_INT >= 33
    }
}
