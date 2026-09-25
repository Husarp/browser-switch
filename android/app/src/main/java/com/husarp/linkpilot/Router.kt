package com.husarp.linkpilot

import android.content.Context

// Where a link goes: the first rule that matches (rules win over the live category), otherwise the
// live category. A rule whose category is gone, or whose browser is not installed, is passed over,
// so a link is never sent nowhere.
object Router {
    class Choice(val category: Category?, val why: String)

    fun decide(ctx: Context, store: Store, url: String, fromApp: String?): Choice {
        val cats = store.categories
        if (store.rulesOn) for (r in store.rules) {
            if (!r.on) continue
            val c = cats.firstOrNull { it.name.equals(r.category, ignoreCase = true) } ?: continue
            if (!Browsers.installed(ctx, c)) continue
            val hit = if (r.byApp) fromApp != null && r.match.split(';').any { it.trim().equals(fromApp, ignoreCase = true) }
                      else hasAddress(r.match, url)
            if (hit) return Choice(c, "${c.name} (rule: ${r.describe()})")
        }
        val live = store.live()
        return Choice(live, if (live != null) "live category ${live.name}" else "")
    }

    // "github.com" matches github.com and any site under it (gist.github.com, www.github.com). Text
    // with a "/" in it is looked for anywhere in the address ("github.com/my-company").
    fun hasAddress(pattern: String, url: String): Boolean {
        var p = pattern.trim().lowercase()
        if (p.isEmpty() || url.isEmpty()) return false
        val u = url.lowercase()
        if (p.contains('/')) return u.contains(p)
        if (p.startsWith("*.")) p = p.substring(2)
        val host = Cleaner.hostOf(url) ?: return u.contains(p)
        return host == p || host.endsWith(".$p")
    }

    // What someone typed for an address, as a rule matches it: "https://github.com/" -> "github.com".
    fun cleanAddress(text: String): String {
        var t = text.trim()
        for (scheme in listOf("https://", "http://")) if (t.startsWith(scheme, ignoreCase = true)) t = t.substring(scheme.length)
        return t.trimEnd('/')
    }
}
