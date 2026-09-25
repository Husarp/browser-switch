package com.husarp.linkpilot

import android.app.Activity
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Intent
import android.os.Bundle
import android.widget.Toast

// Share -> "Copy clean link": the first link in what was shared, cleaned, is copied, ready to paste.
// (Android lets no app watch the clipboard, so copied links cannot be cleaned by themselves as on
// Windows - sharing is the way in.)
class ShareActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val text = intent?.getStringExtra(Intent.EXTRA_TEXT) ?: ""
        val link = Regex("https?://\\S+", RegexOption.IGNORE_CASE).find(text)?.value
        if (link == null) {
            Toast.makeText(this, "No link to copy in that", Toast.LENGTH_SHORT).show()
            finish()
            return
        }
        val store = Store(this)
        val cleaned = Cleaner.apply(link, store.cleanOptions())
        getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText("link", cleaned.url))
        val from = referrer?.takeIf { it.scheme == "android-app" }?.host ?: ""
        store.addLog(from, "(copied)", "shared - copied, not opened", link, cleaned.url, cleaned.changes)
        Toast.makeText(this, if (cleaned.changes.isEmpty()) "Link copied - nothing to clean" else "Clean link copied - ${cleaned.changes}",
                       Toast.LENGTH_LONG).show()
        finish()
    }
}
