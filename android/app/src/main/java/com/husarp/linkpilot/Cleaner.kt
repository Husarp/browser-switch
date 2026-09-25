package com.husarp.linkpilot

import java.net.URLDecoder

// Link cleaning - the same lists and the same way as LinkPilot for Windows (Cleaner.cs):
//
//   Redirects   Some links go through a middleman first - google.com/url?q=<the real link>, Outlook's
//               Safe Links, Facebook's l.php... - so that the middleman learns where you went. The
//               real link is written inside, so it can be taken out and opened directly.
//   Tracking    Parts added to the end of a link to say where the click came from: utm_source,
//               fbclid, YouTube's si... The page is the same without them.
//
// Only parts known to be tracking are removed, so a link never stops working. Nothing is looked up
// online - which is also why short links (bit.ly, t.co) cannot be followed.
object Cleaner {
    // A tracking part: its name ("utm_*" is every name starting with utm_), the sites it is removed
    // on ("" = every site; "amazon.*" = Amazon in every country), and what it is.
    class Part(val name: String, val sites: String, val what: String) {
        val id get() = if (sites.isEmpty()) name else "$name@$sites"
    }

    // A middleman: the address it lives at and the part of the link that holds the real one.
    class Redirect(val host: String, val path: String, val param: String, val what: String) {
        val id get() = host + path
    }

    class Options(
        val cleanOn: Boolean = true,
        val unwrapOn: Boolean = true,
        val partsOff: Set<String> = emptySet(),
        val redirectsOff: Set<String> = emptySet(),
    ) {
        fun isOn(p: Part) = partsOff.none { it.equals(p.id, ignoreCase = true) }
        fun isOn(r: Redirect) = redirectsOff.none { it.equals(r.id, ignoreCase = true) }
    }

    // The link as it will be opened, and what was done to it ("skipped Google redirect; removed
    // utm_source, fbclid"), or "" if nothing.
    class Result(val url: String, val changes: String)

    val parts = listOf(
        Part("utm_*", "", "Campaign tracking - Google Analytics, newsletters, most websites"),
        Part("fbclid", "", "Facebook click ID"),
        Part("gclid", "", "Google Ads click ID"),
        Part("gclsrc", "", "Google Ads click source"),
        Part("dclid", "", "Google Display Ads click ID"),
        Part("gbraid", "", "Google Ads click ID (apps)"),
        Part("wbraid", "", "Google Ads click ID (web)"),
        Part("msclkid", "", "Microsoft Ads click ID"),
        Part("twclid", "", "X / Twitter Ads click ID"),
        Part("ttclid", "", "TikTok Ads click ID"),
        Part("yclid", "", "Yandex Ads click ID"),
        Part("li_fat_id", "", "LinkedIn Ads click ID"),
        Part("igshid", "", "Instagram share tracking"),
        Part("igsh", "", "Instagram share tracking"),
        Part("mc_cid", "", "Mailchimp campaign"),
        Part("mc_eid", "", "Mailchimp subscriber - says who you are"),
        Part("_hsenc", "", "HubSpot email tracking"),
        Part("_hsmi", "", "HubSpot email tracking"),
        Part("mkt_tok", "", "Marketo email tracking"),
        Part("si", "youtube.com youtu.be open.spotify.com", "Share tracking - who shared the link with you"),
        Part("pp", "youtube.com", "YouTube search tracking"),
        Part("feature", "youtube.com youtu.be", "Where on YouTube the link was shared from"),
        Part("ref_src", "twitter.com x.com", "X / Twitter referral"),
        Part("ref_url", "twitter.com x.com", "X / Twitter referral"),
        Part("ref_", "amazon.*", "Amazon referral"),
        Part("pd_rd_*", "amazon.*", "Amazon recommendation tracking"),
        Part("pf_rd_*", "amazon.*", "Amazon page tracking"),
        Part("crid", "amazon.*", "Amazon search tracking"),
        Part("sprefix", "amazon.*", "Amazon search tracking"),
        Part("qid", "amazon.*", "Amazon search tracking"),
        Part("sr", "amazon.*", "Amazon search tracking"),
        Part("dib", "amazon.*", "Amazon search tracking"),
        Part("dib_tag", "amazon.*", "Amazon search tracking"),
        Part("content-id", "amazon.*", "Amazon page tracking"),
        // online shops: from ClearURLs' rules (clearurls.xyz), plus the share-link parts of Temu and Shein
        Part("__mk_*", "amazon.*", "Amazon language tracking"),
        Part("spIA", "amazon.*", "Amazon tracking"),
        Part("ms3_c", "amazon.*", "Amazon tracking"),
        Part("refRID", "amazon.*", "Amazon tracking"),
        Part("_encoding", "amazon.*", "Amazon tracking"),
        Part("smid", "amazon.*", "Amazon seller tracking"),
        Part("rnid", "amazon.*", "Amazon tracking"),
        Part("dchild", "amazon.*", "Amazon tracking"),
        Part("aaxitk", "amazon.*", "Amazon ad tracking"),
        Part("hsa_cr_id", "amazon.*", "Amazon ad tracking"),
        Part("sb-ci-*", "amazon.*", "Amazon ad tracking"),
        Part("social_share", "amazon.*", "Amazon share tracking"),
        Part("starsLeft", "amazon.*", "Amazon tracking"),
        Part("skipTwisterOG", "amazon.*", "Amazon tracking"),
        Part("linkCode", "amazon.*", "Amazon affiliate tracking"),
        Part("linkId", "amazon.*", "Amazon affiliate tracking"),
        Part("creativeASIN", "amazon.*", "Amazon affiliate tracking"),
        Part("ascsubtag", "amazon.*", "Amazon affiliate tracking"),
        Part("camp", "amazon.*", "Amazon affiliate tracking"),
        Part("creative", "amazon.*", "Amazon affiliate tracking"),
        Part("_trkparms", "ebay.*", "eBay tracking"),
        Part("_trksid", "ebay.*", "eBay tracking"),
        Part("_from", "ebay.*", "eBay tracking"),
        Part("hash", "ebay.*", "eBay tracking"),
        Part("amdata", "ebay.*", "eBay tracking"),
        Part("mkcid", "ebay.*", "eBay marketing tracking"),
        Part("mkevt", "ebay.*", "eBay marketing tracking"),
        Part("mkrid", "ebay.*", "eBay marketing tracking"),
        Part("campid", "ebay.*", "eBay affiliate tracking"),
        Part("toolid", "ebay.*", "eBay affiliate tracking"),
        Part("customid", "ebay.*", "eBay affiliate tracking"),
        Part("spm", "aliexpress.*", "AliExpress tracking"),
        Part("scm*", "aliexpress.*", "AliExpress tracking"),
        Part("pvid", "aliexpress.*", "AliExpress tracking"),
        Part("algo_*", "aliexpress.*", "AliExpress tracking"),
        Part("ws_ab_test", "aliexpress.*", "AliExpress tracking"),
        Part("btsid", "aliexpress.*", "AliExpress tracking"),
        Part("gps-id", "aliexpress.*", "AliExpress tracking"),
        Part("cv", "aliexpress.*", "AliExpress tracking"),
        Part("af", "aliexpress.*", "AliExpress tracking"),
        Part("dp", "aliexpress.*", "AliExpress tracking"),
        Part("sk", "aliexpress.*", "AliExpress share tracking"),
        Part("mall_affr", "aliexpress.*", "AliExpress affiliate tracking"),
        Part("terminal_id", "aliexpress.*", "AliExpress tracking - which device"),
        Part("aff_*", "aliexpress.*", "AliExpress affiliate tracking"),
        Part("afSmartRedirect", "aliexpress.*", "AliExpress affiliate tracking"),
        Part("srcSns", "aliexpress.*", "AliExpress share tracking"),
        Part("spreadType", "aliexpress.*", "AliExpress share tracking"),
        Part("bizType", "aliexpress.*", "AliExpress share tracking"),
        Part("social_params", "aliexpress.*", "AliExpress share tracking"),
        Part("pdp_npi", "aliexpress.*", "AliExpress tracking - the price you saw"),
        Part("pdp_ext_f", "aliexpress.*", "AliExpress tracking"),
        Part("gatewayAdapt", "aliexpress.*", "AliExpress tracking"),
        Part("bi_*", "allegro.*", "Allegro ad and listing tracking"),
        Part("reco_id", "allegro.*", "Allegro recommendation tracking"),
        Part("sid", "allegro.*", "Allegro session tracking"),
        Part("emission_unit_id", "allegro.*", "Allegro ad tracking"),
        Part("emission_id", "allegro.*", "Allegro ad tracking"),
        Part("_x_*", "temu.*", "Temu tracking"),
        Part("refer_page_*", "temu.*", "Temu tracking - where you came from"),
        Part("share_uin", "temu.*", "Temu share tracking - who shared it"),
        Part("_bg_fs", "temu.*", "Temu tracking"),
        Part("_oak_*", "temu.*", "Temu tracking"),
        Part("_p_rfs", "temu.*", "Temu tracking"),
        Part("src_module", "shein.*", "Shein tracking"),
        Part("src_identifier", "shein.*", "Shein tracking"),
        Part("src_tab_page_id", "shein.*", "Shein tracking"),
        Part("url_from", "shein.*", "Shein share tracking"),
        Part("click_key", "etsy.com", "Etsy tracking"),
        Part("click_sum", "etsy.com", "Etsy tracking"),
        Part("organic_search_click", "etsy.com", "Etsy tracking"),
        Part("ref", "etsy.com", "Etsy referral"),
        Part("ga_*", "etsy.com", "Etsy tracking"),
        Part("u1", "walmart.*", "Walmart tracking"),
        Part("ath*", "walmart.*", "Walmart ad tracking"),
        Part("tag", "ceneo.pl", "Ceneo tracking"),
    )

    val redirects = listOf(
        Redirect("google.*", "/url", "q url", "Google search results and Gmail"),
        Redirect("googleadservices.com", "/pagead/aclk", "adurl", "Google Ads"),
        Redirect("safelinks.protection.outlook.com", "/", "url", "Outlook Safe Links"),
        Redirect("statics.teams.cdn.office.net", "/evergreen-assets/safelinks/", "url", "Microsoft Teams Safe Links"),
        Redirect("l.facebook.com", "/l.php", "u", "Facebook"),
        Redirect("lm.facebook.com", "/l.php", "u", "Facebook (mobile)"),
        Redirect("l.messenger.com", "/l.php", "u", "Messenger"),
        Redirect("l.instagram.com", "/", "u", "Instagram"),
        Redirect("youtube.com", "/redirect", "q", "YouTube descriptions and comments"),
        Redirect("steamcommunity.com", "/linkfilter/", "u url", "Steam"),
        Redirect("linkedin.com", "/safety/go", "url", "LinkedIn"),
        Redirect("duckduckgo.com", "/l/", "uddg", "DuckDuckGo"),
        Redirect("vk.com", "/away.php", "to", "VK"),
        Redirect("slack-redir.net", "/link", "url", "Slack"),
        Redirect("out.reddit.com", "/", "url", "Reddit"),
    )

    fun isWebLink(url: String) = url.startsWith("http://", ignoreCase = true) || url.startsWith("https://", ignoreCase = true)

    fun apply(link: String, o: Options): Result {
        if (!isWebLink(link)) return Result(link, "")
        var url = link
        val said = mutableListOf<String>()
        try {
            // a middleman can wrap another (Outlook Safe Links around a Google link): unwrap in turns
            if (o.unwrapOn) for (i in 0 until 5) {
                val (inner, what) = unwrap(url, o) ?: break
                said += "skipped $what redirect"
                url = inner
            }
            if (o.cleanOn) {
                val removed = mutableListOf<String>()
                url = removeTracking(url, removed, o)
                if (removed.isNotEmpty()) said += "removed " + removed.distinct().joinToString(", ")
            }
        } catch (_: Exception) {
            // a link this cannot read is opened as it came
        }
        return Result(url, said.joinToString("; "))
    }

    // The site of a link, "www.youtube.com" - or null if it is not an address with one.
    fun hostOf(url: String): String? = split(url)?.host

    private class Pieces(val host: String, val path: String, val query: String)

    // scheme://[user@]host[:port]/path?query#fragment - read by hand, so an odd character
    // somewhere in a link does not stop it being read.
    private fun split(url: String): Pieces? {
        val s = url.indexOf("://")
        if (s <= 0) return null
        val rest = url.substring(s + 3)
        val end = rest.indexOfFirst { it == '/' || it == '?' || it == '#' }.let { if (it < 0) rest.length else it }
        val host = rest.substring(0, end).substringAfterLast('@').substringBefore(':').lowercase()
        if (host.isEmpty()) return null
        val after = rest.substring(end).substringBefore('#')
        val path = after.substringBefore('?').ifEmpty { "/" }
        val query = if (after.contains('?')) after.substringAfter('?') else ""
        return Pieces(host, path, query)
    }

    // %XX written out; a "+" stays a "+"; anything that is not proper %-writing stays as it is.
    private fun unescape(s: String): String =
        if (!s.contains('%')) s
        else try { URLDecoder.decode(s.replace("+", "%2B"), "UTF-8") } catch (_: Exception) { s }

    // The link inside a middleman's link, and the middleman's name - or null if it is not one.
    private fun unwrap(url: String, o: Options): Pair<String, String>? {
        val p = split(url) ?: return null
        for (r in redirects) {
            if (!o.isOn(r) || !onSite(p.host, r.host)) continue
            if (r.path != "/" && !p.path.startsWith(r.path, ignoreCase = true)) continue
            for (name in r.param.split(' ')) {
                val value = unescape(queryValue(p.query, name) ?: continue).trim()
                // only ever a real web address - never a script or a file hidden in the parameter
                if (isWebLink(value) && split(value) != null) return value to r.what.split(' ')[0].trimEnd(',')
            }
        }
        return null
    }

    private fun queryValue(query: String, name: String): String? {
        for (pair in query.split('&')) {
            val eq = pair.indexOf('=')
            if (eq > 0 && pair.substring(0, eq).equals(name, ignoreCase = true)) return pair.substring(eq + 1)
        }
        return null
    }

    // Takes the tracking parts out of the query (after the ?), leaving everything else - the other
    // parts, their order and their exact spelling, and anything after a # - untouched. Amazon also
    // writes its tracking into the path itself, "/ref=sr_1_3", which goes too.
    private fun removeTracking(link: String, removed: MutableList<String>, o: Options): String {
        var url = link
        var fragment = ""
        val hash = url.indexOf('#')
        if (hash >= 0) { fragment = url.substring(hash); url = url.substring(0, hash) }
        var query = ""
        val q = url.indexOf('?')
        if (q >= 0) { query = url.substring(q + 1); url = url.substring(0, q) }

        val host = hostOf(url) ?: ""
        val active = parts.filter { o.isOn(it) && (it.sites.isEmpty() || it.sites.split(' ').any { s -> onSite(host, s) }) }

        if (host.isNotEmpty() && onSite(host, "amazon.*") && active.any { it.name == "ref_" }) {
            val at = url.indexOf("/ref=", ignoreCase = true)
            if (at > url.indexOf("//") + 1) { url = url.substring(0, at); removed += "/ref=" }
        }

        if (query.isNotEmpty()) {
            val kept = mutableListOf<String>()
            for (pair in query.split('&')) {
                if (pair.isEmpty()) continue
                val eq = pair.indexOf('=')
                val name = unescape(if (eq >= 0) pair.substring(0, eq) else pair)
                if (active.any { matches(it.name, name) }) removed += name else kept += pair
            }
            if (kept.isNotEmpty()) url += "?" + kept.joinToString("&")
        }
        return url + fragment
    }

    private fun matches(pattern: String, name: String) =
        if (pattern.endsWith("*")) name.startsWith(pattern.trimEnd('*'), ignoreCase = true)
        else pattern.equals(name, ignoreCase = true)

    // "youtube.com" is youtube.com and every site under it (www., m., music.); "amazon.*" is Amazon
    // in every country (amazon.de, amazon.co.uk, www.amazon.pl).
    fun onSite(host: String, site: String): Boolean =
        if (site.endsWith(".*")) ".$host.".contains("." + site.dropLast(2) + ".")
        else host == site || host.endsWith(".$site")
}
