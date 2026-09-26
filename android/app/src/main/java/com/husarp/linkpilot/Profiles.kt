package com.husarp.linkpilot

import android.app.Activity
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.pm.CrossProfileApps
import android.content.pm.LauncherApps
import android.graphics.drawable.Drawable
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Process
import android.os.UserHandle
import android.os.UserManager

// Browsers in another profile of this phone - the work profile Island makes, for one.
//
// Android keeps profiles apart: an app in one cannot open an app in the other. The one official way
// across is for an app to start *itself* in the other profile (Android 11+, "connected work & personal
// apps"). So LinkPilot has to be installed in both - Island calls that cloning - and allowed to
// connect; then this copy hands the link to the copy over there (ProfileOpenActivity), which opens it
// in the chosen browser, inside that profile.
//
// The other way round, the copy in the work profile decides nothing itself: every link tapped in a work
// app reaches it (it is the work profile's default browser) and it passes the link - with the app it
// came from - to the copy in the personal profile (relay), whose categories and rules then decide. So
// everything is set up in one place, and rules can name apps of either profile.
//
// A category keeps the profile by its serial number, which stays the same across restarts.
object Profiles {
    enum class State {
        TooOld,          // Android 10 or older: no way across
        NotInstalled,    // LinkPilot is not in that profile yet
        NotAllowed,      // not allowed to connect, and Android offers no switch for it
        CanAsk,          // not allowed yet; Android can show the switch
        Ready,
    }

    private fun launcher(ctx: Context) = ctx.getSystemService(LauncherApps::class.java)
    private fun users(ctx: Context) = ctx.getSystemService(UserManager::class.java)

    // The other profiles on this phone (usually none, or one work profile).
    fun others(ctx: Context): List<UserHandle> =
        try { launcher(ctx).profiles.filter { it != Process.myUserHandle() } } catch (_: Exception) { emptyList() }

    fun serial(ctx: Context, user: UserHandle): Long = users(ctx).getSerialNumberForUser(user)
    fun user(ctx: Context, serial: Long): UserHandle? = users(ctx).getUserForSerialNumber(serial)

    fun state(ctx: Context, user: UserHandle): State {
        if (Build.VERSION.SDK_INT < 30) return State.TooOld
        val cross = ctx.getSystemService(CrossProfileApps::class.java)
        if (user !in cross.targetUserProfiles) return State.NotInstalled
        if (cross.canInteractAcrossProfiles()) return State.Ready
        return if (cross.canRequestInteractAcrossProfiles()) State.CanAsk else State.NotAllowed
    }

    // Android's own page with the "Allow connection" switch for LinkPilot.
    fun askIntent(ctx: Context): Intent? =
        if (Build.VERSION.SDK_INT >= 30) try { ctx.getSystemService(CrossProfileApps::class.java).createRequestInteractAcrossProfilesIntent() } catch (_: Exception) { null }
        else null

    // This copy is the one in the work profile.
    fun isWorkCopy(ctx: Context): Boolean = Build.VERSION.SDK_INT >= 30 && users(ctx).isManagedProfile

    // In the work profile: the personal profile, if the copy there can be reached - else null, and
    // this copy decides by itself, with its own settings.
    fun relayTarget(ctx: Context): UserHandle? {
        if (Build.VERSION.SDK_INT < 30 || !isWorkCopy(ctx)) return null
        val cross = ctx.getSystemService(CrossProfileApps::class.java)
        return if (cross.canInteractAcrossProfiles()) cross.targetUserProfiles.firstOrNull() else null
    }

    // Hands a link tapped in a work app to LinkPilot in the personal profile, saying which app.
    fun relay(activity: Activity, user: UserHandle, url: String, from: String?): Boolean {
        if (Build.VERSION.SDK_INT < 30) return false
        return try {
            val intent = Intent(Intent.ACTION_VIEW, Uri.parse(url))
                .setComponent(ComponentName(activity, LinkActivity::class.java))
                .putExtra(FROM_PROFILE, serial(activity, Process.myUserHandle()))
            if (from != null) intent.putExtra(FROM_APP, from)
            activity.getSystemService(CrossProfileApps::class.java).startActivity(intent, user, activity)
            true
        } catch (_: Exception) {
            false
        }
    }
    const val FROM_APP = "com.husarp.linkpilot.FROM_APP"
    const val FROM_PROFILE = "com.husarp.linkpilot.FROM_PROFILE"

    // An app of either profile, as the link log and the rules keep it: "org.thoughtcrime.securesms",
    // or "org.thoughtcrime.securesms@10" for Signal in the work profile (its serial number).
    fun appKey(pkg: String, profile: Long?) = if (profile == null) pkg else "$pkg@$profile"
    fun splitKey(key: String): Pair<String, Long?> =
        key.substringBefore('@') to key.substringAfter('@', "").toLongOrNull()

    // Island's own screen, where LinkPilot is cloned into the work profile (Island lets no other app
    // do that) - null if Island is not installed.
    fun islandIntent(ctx: Context): Intent? =
        listOf("com.oasisfeng.island", "com.oasisfeng.island.fdroid").firstNotNullOfOrNull { ctx.packageManager.getLaunchIntentForPackage(it) }
            ?.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)

    // The apps on that profile's home screen, as the launcher sees them - with the work badge.
    fun apps(ctx: Context, user: UserHandle): List<Browsers.App> =
        try {
            val s = serial(ctx, user)
            launcher(ctx).getActivityList(null, user).map { Browsers.App(it.componentName.packageName, it.label.toString(), s) }
                .distinctBy { it.pkg }.filter { it.pkg != ctx.packageName }.sortedBy { it.label.lowercase() }
        } catch (_: Exception) { emptyList() }

    // Which of them are browsers. Android does not let one profile ask what the other's apps can
    // open, so this goes by the browsers it knows by name, and those that are browsers here too.
    fun isBrowser(ctx: Context, pkg: String) =
        pkg in knownBrowsers || Browsers.all(ctx).any { it.pkg == pkg }

    private val knownBrowsers = setOf(
        "com.android.chrome", "com.chrome.beta", "org.chromium.chrome", "org.mozilla.firefox", "org.mozilla.fenix",
        "org.mozilla.firefox_beta", "org.mozilla.focus", "org.mozilla.fennec_fdroid", "us.spotco.fennec_dos",
        "org.ironfoxoss.ironfox", "com.brave.browser", "com.microsoft.emmx", "com.duckduckgo.mobile.android",
        "com.opera.browser", "com.opera.mini.native", "com.opera.gx", "com.vivaldi.browser", "com.sec.android.app.sbrowser",
        "org.torproject.torbrowser", "com.kiwibrowser.browser", "org.bromite.bromite", "org.cromite.cromite",
        "com.yandex.browser", "app.vanadium.browser", "org.lineageos.jelly", "com.ecosia.android", "com.qwant.liberty",
        "mark.via.gp", "com.mi.globalbrowser", "com.UCMobile.intl", "com.cloudmosa.puffinFree",
    )

    fun label(ctx: Context, pkg: String, user: UserHandle): String =
        try { launcher(ctx).getActivityList(pkg, user).firstOrNull()?.label?.toString() } catch (_: Exception) { null } ?: pkg

    fun icon(ctx: Context, pkg: String, user: UserHandle): Drawable? =
        try { launcher(ctx).getActivityList(pkg, user).firstOrNull()?.getBadgedIcon(0) } catch (_: Exception) { null }

    fun installed(ctx: Context, pkg: String, user: UserHandle): Boolean =
        try { launcher(ctx).getActivityList(pkg, user).isNotEmpty() } catch (_: Exception) { false }

    // Hands the link to LinkPilot in that profile, to open in this browser there.
    fun open(activity: Activity, user: UserHandle, pkg: String, url: String): Boolean {
        if (Build.VERSION.SDK_INT < 30) return false
        return try {
            val cross = activity.getSystemService(CrossProfileApps::class.java)
            if (!cross.canInteractAcrossProfiles()) return false
            val intent = Intent(Intent.ACTION_VIEW, Uri.parse(url))
                .setComponent(ComponentName(activity, ProfileOpenActivity::class.java))
                .putExtra(ProfileOpenActivity.BROWSER, pkg)
            cross.startActivity(intent, user, activity)
            true
        } catch (_: Exception) {
            false
        }
    }
}

// LinkPilot in the other profile: opens the link it was handed, in the browser it was told -
// inside this profile. Nothing shows.
class ProfileOpenActivity : Activity() {
    companion object { const val BROWSER = "com.husarp.linkpilot.BROWSER" }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val url = intent?.data
        val pkg = intent?.getStringExtra(BROWSER)
        if (url != null && url.scheme.let { it == "http" || it == "https" }) {
            val view = Intent(Intent.ACTION_VIEW, url).addCategory(Intent.CATEGORY_BROWSABLE).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            try { startActivity(Intent(view).setPackage(pkg)) }
            catch (_: Exception) { try { startActivity(view) } catch (_: Exception) { } }   // that browser is gone: Android asks
        }
        finish()
    }
}
