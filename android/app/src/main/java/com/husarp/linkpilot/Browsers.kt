package com.husarp.linkpilot

import android.app.role.RoleManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.pm.ResolveInfo
import android.graphics.drawable.Drawable
import android.net.Uri
import android.os.Build

// The browsers on this phone, and the apps (for app rules) - by package name, "org.mozilla.firefox".
object Browsers {
    class App(val pkg: String, val label: String, val profile: Long? = null)   // profile: an app in the work profile

    private fun webIntent() = Intent(Intent.ACTION_VIEW, Uri.parse("https://example.com")).addCategory(Intent.CATEGORY_BROWSABLE)

    @Suppress("DEPRECATION")
    private fun query(ctx: Context, intent: Intent, flags: Int): List<ResolveInfo> =
        if (Build.VERSION.SDK_INT >= 33) ctx.packageManager.queryIntentActivities(intent, PackageManager.ResolveInfoFlags.of(flags.toLong()))
        else ctx.packageManager.queryIntentActivities(intent, flags)

    // Every app that opens web pages, except LinkPilot itself.
    fun all(ctx: Context): List<App> =
        query(ctx, webIntent(), PackageManager.MATCH_ALL)
            .map { it.activityInfo.packageName }.distinct()
            .filter { it != ctx.packageName }
            .map { App(it, label(ctx, it)) }
            .sortedBy { it.label.lowercase() }

    // The apps on the home screen, for app rules.
    fun apps(ctx: Context): List<App> =
        query(ctx, Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER), 0)
            .map { it.activityInfo.packageName }.distinct()
            .filter { it != ctx.packageName }
            .map { App(it, label(ctx, it)) }
            .sortedBy { it.label.lowercase() }

    fun label(ctx: Context, pkg: String): String = try {
        val pm = ctx.packageManager
        pm.getApplicationLabel(pm.getApplicationInfo(pkg, 0)).toString()
    } catch (_: Exception) {
        pkg
    }

    fun installed(ctx: Context, pkg: String): Boolean = try {
        ctx.packageManager.getApplicationInfo(pkg, 0); true
    } catch (_: Exception) {
        false
    }

    // A category's browser - here, or in another profile (Profiles.kt).
    fun label(ctx: Context, c: Category): String {
        val user = c.profile?.let { Profiles.user(ctx, it) } ?: return label(ctx, c.pkg)
        return Profiles.label(ctx, c.pkg, user) + " (work profile)"
    }

    fun installed(ctx: Context, c: Category): Boolean {
        if (c.profile == null) return installed(ctx, c.pkg)
        val user = Profiles.user(ctx, c.profile) ?: return false
        return Profiles.installed(ctx, c.pkg, user)
    }

    fun icon(ctx: Context, c: Category): Drawable? {
        val user = c.profile?.let { Profiles.user(ctx, it) }
        if (c.profile != null) return user?.let { Profiles.icon(ctx, c.pkg, it) }
        return try { ctx.packageManager.getApplicationIcon(c.pkg) } catch (_: Exception) { null }
    }

    fun isDefault(ctx: Context): Boolean {
        if (Build.VERSION.SDK_INT >= 29) {
            val roles = ctx.getSystemService(RoleManager::class.java)
            if (roles != null && roles.isRoleAvailable(RoleManager.ROLE_BROWSER)) return roles.isRoleHeld(RoleManager.ROLE_BROWSER)
        }
        return defaultPackage(ctx) == ctx.packageName
    }

    // The browser links go to now, if it is a browser other than LinkPilot; null if Android
    // asks each time, or LinkPilot is the default.
    fun currentDefault(ctx: Context): String? {
        val p = defaultPackage(ctx) ?: return null
        return if (p == ctx.packageName || all(ctx).none { it.pkg == p }) null else p
    }

    @Suppress("DEPRECATION")
    private fun defaultPackage(ctx: Context): String? =
        ctx.packageManager.resolveActivity(webIntent(), PackageManager.MATCH_DEFAULT_ONLY)?.activityInfo?.packageName
}
