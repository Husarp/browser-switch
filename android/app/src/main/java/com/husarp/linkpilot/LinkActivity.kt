package com.husarp.linkpilot

import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.widget.Toast

// Every link, while LinkPilot is the default browser: cleaned, sent by the rules or to the live
// category's browser, written in the link log - and nothing shows. Only if there is no browser to
// send it to (no category yet, or its browser was uninstalled) is a list of browsers shown.
// In the work profile it only passes the link on, to the copy in the personal profile (Profiles.kt).
class LinkActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val asked = intent?.dataString
        if (asked == null) { finish(); return }

        val store = Store(this)
        // Android says which app sent the link: android-app://org.thoughtcrime.securesms
        val referred = referrer?.takeIf { it.scheme == "android-app" }?.host?.takeIf { it != packageName }
        // A link tapped in a work app, passed here by LinkPilot in the work profile: its serial number
        val workProfile = intent.getLongExtra(Profiles.FROM_PROFILE, -1).takeIf { it >= 0 && !Profiles.isWorkCopy(this) }
        // In the work profile: the copy in the personal profile decides, if it can be reached
        if (workProfile == null) Profiles.relayTarget(this)?.let { personal ->
            if (Profiles.relay(this, personal, asked, referred)) { finish(); return }
        }
        val from = if (workProfile != null) intent.getStringExtra(Profiles.FROM_APP) else referred
        val fromKey = from?.let { Profiles.appKey(it, workProfile) } ?: ""
        if (fromKey.isNotEmpty()) store.rememberApp(fromKey)

        val cleaned = Cleaner.apply(asked, store.cleanOptions())
        val choice = Router.decide(this, store, cleaned.url, from, workProfile)
        val target = choice.category
        if (target != null && Browsers.installed(this, target) && open(target, cleaned.url)) {
            store.addLog(fromKey, target.name, choice.why, asked, cleaned.url, cleaned.changes)
            sayCleaned(cleaned)
            finish()
            return
        }
        ask(store, fromKey, asked, cleaned)
    }

    // A browser in the work profile gets the link through LinkPilot over there (Profiles.kt).
    private fun open(c: Category, url: String): Boolean {
        val user = c.profile?.let { Profiles.user(this, it) }
        return if (c.profile != null) user != null && Profiles.open(this, user, c.pkg, url) else open(c.pkg, url)
    }

    private fun open(pkg: String, url: String): Boolean = try {
        startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).setPackage(pkg)
            .addCategory(Intent.CATEGORY_BROWSABLE).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        true
    } catch (_: Exception) {
        false
    }

    // Only when something was taken out: "Link cleaned - removed utm_source".
    private fun sayCleaned(cleaned: Cleaner.Result) {
        if (cleaned.changes.isNotEmpty())
            Toast.makeText(applicationContext, "Link cleaned - ${cleaned.changes}", Toast.LENGTH_SHORT).show()
    }

    private fun ask(store: Store, from: String, asked: String, cleaned: Cleaner.Result) {
        val browsers = Browsers.all(this)
        if (browsers.isEmpty()) { finish(); return }
        AlertDialog.Builder(this, android.R.style.Theme_DeviceDefault_Dialog_Alert)
            .setTitle("Open in which browser?")
            .setItems(browsers.map { it.label }.toTypedArray()) { _, i ->
                if (open(browsers[i].pkg, cleaned.url)) {
                    store.addLog(from, browsers[i].label, "chosen - no category to send it to", asked, cleaned.url, cleaned.changes)
                    sayCleaned(cleaned)
                }
            }
            .setOnDismissListener { finish() }
            .show()
    }
}
