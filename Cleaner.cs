// Link cleaning: before a link is handed on, two things can be taken out of it.
//
//   Redirects   Some links go through a middleman first - google.com/url?q=<the real link>,
//               Outlook's Safe Links, Facebook's l.php... - so that the middleman learns where you
//               went. The real link is written inside, so it can be taken out and opened directly.
//   Tracking    Parts added to the end of a link to say where the click came from: utm_source,
//               fbclid, YouTube's si... The page is the same without them.
//
// Only parts known to be tracking are removed, so a link never stops working. Everything happens on
// this computer: nothing is looked up online - which is also why short links (bit.ly, t.co) cannot
// be followed: where they lead is only known to their server.
//
// It works on links that come from other programs - email, chat, documents. A link clicked inside a
// browser never leaves it, so LinkPilot never sees it; that needs a browser extension.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Cleaner
{
    // A tracking part: its name ("utm_*" is every name starting with utm_), the sites it is removed
    // on ("" = every site; "amazon.*" = Amazon in every country), and what it is.
    public class Part
    {
        public string Name, Sites, What;
        public bool Custom;
        public string Id { get { return Sites.Length == 0 ? Name : Name + "@" + Sites; } }
    }

    // A middleman: the address it lives at and the part of the link that holds the real one.
    public class Redirect
    {
        public string Host, Path, Param, What;
        public string Id { get { return Host + Path; } }
    }

    static Part P(string name, string sites, string what) { return new Part { Name = name, Sites = sites, What = what }; }
    static Redirect R(string host, string path, string param, string what) { return new Redirect { Host = host, Path = path, Param = param, What = what }; }

    public static readonly List<Part> BuiltIn = new List<Part> {
        P("utm_*", "", "Campaign tracking - Google Analytics, newsletters, most websites"),
        P("fbclid", "", "Facebook click ID"),
        P("gclid", "", "Google Ads click ID"),
        P("gclsrc", "", "Google Ads click source"),
        P("dclid", "", "Google Display Ads click ID"),
        P("gbraid", "", "Google Ads click ID (apps)"),
        P("wbraid", "", "Google Ads click ID (web)"),
        P("msclkid", "", "Microsoft Ads click ID"),
        P("twclid", "", "X / Twitter Ads click ID"),
        P("ttclid", "", "TikTok Ads click ID"),
        P("yclid", "", "Yandex Ads click ID"),
        P("li_fat_id", "", "LinkedIn Ads click ID"),
        P("igshid", "", "Instagram share tracking"),
        P("igsh", "", "Instagram share tracking"),
        P("mc_cid", "", "Mailchimp campaign"),
        P("mc_eid", "", "Mailchimp subscriber - says who you are"),
        P("_hsenc", "", "HubSpot email tracking"),
        P("_hsmi", "", "HubSpot email tracking"),
        P("mkt_tok", "", "Marketo email tracking"),
        P("si", "youtube.com youtu.be open.spotify.com", "Share tracking - who shared the link with you"),
        P("pp", "youtube.com", "YouTube search tracking"),
        P("feature", "youtube.com youtu.be", "Where on YouTube the link was shared from"),
        P("ref_src", "twitter.com x.com", "X / Twitter referral"),
        P("ref_url", "twitter.com x.com", "X / Twitter referral"),
        P("ref_", "amazon.*", "Amazon referral"),
        P("pd_rd_*", "amazon.*", "Amazon recommendation tracking"),
        P("pf_rd_*", "amazon.*", "Amazon page tracking"),
        P("crid", "amazon.*", "Amazon search tracking"),
        P("sprefix", "amazon.*", "Amazon search tracking"),
        P("qid", "amazon.*", "Amazon search tracking"),
        P("sr", "amazon.*", "Amazon search tracking"),
        P("dib", "amazon.*", "Amazon search tracking"),
        P("dib_tag", "amazon.*", "Amazon search tracking"),
        P("content-id", "amazon.*", "Amazon page tracking"),
        // online shops: from ClearURLs' rules (clearurls.xyz), plus the share-link parts of Temu and Shein
        P("__mk_*", "amazon.*", "Amazon language tracking"),
        P("spIA", "amazon.*", "Amazon tracking"),
        P("ms3_c", "amazon.*", "Amazon tracking"),
        P("refRID", "amazon.*", "Amazon tracking"),
        P("_encoding", "amazon.*", "Amazon tracking"),
        P("smid", "amazon.*", "Amazon seller tracking"),
        P("rnid", "amazon.*", "Amazon tracking"),
        P("dchild", "amazon.*", "Amazon tracking"),
        P("aaxitk", "amazon.*", "Amazon ad tracking"),
        P("hsa_cr_id", "amazon.*", "Amazon ad tracking"),
        P("sb-ci-*", "amazon.*", "Amazon ad tracking"),
        P("social_share", "amazon.*", "Amazon share tracking"),
        P("starsLeft", "amazon.*", "Amazon tracking"),
        P("skipTwisterOG", "amazon.*", "Amazon tracking"),
        P("linkCode", "amazon.*", "Amazon affiliate tracking"),
        P("linkId", "amazon.*", "Amazon affiliate tracking"),
        P("creativeASIN", "amazon.*", "Amazon affiliate tracking"),
        P("ascsubtag", "amazon.*", "Amazon affiliate tracking"),
        P("camp", "amazon.*", "Amazon affiliate tracking"),
        P("creative", "amazon.*", "Amazon affiliate tracking"),
        P("_trkparms", "ebay.*", "eBay tracking"),
        P("_trksid", "ebay.*", "eBay tracking"),
        P("_from", "ebay.*", "eBay tracking"),
        P("hash", "ebay.*", "eBay tracking"),
        P("amdata", "ebay.*", "eBay tracking"),
        P("mkcid", "ebay.*", "eBay marketing tracking"),
        P("mkevt", "ebay.*", "eBay marketing tracking"),
        P("mkrid", "ebay.*", "eBay marketing tracking"),
        P("campid", "ebay.*", "eBay affiliate tracking"),
        P("toolid", "ebay.*", "eBay affiliate tracking"),
        P("customid", "ebay.*", "eBay affiliate tracking"),
        P("spm", "aliexpress.*", "AliExpress tracking"),
        P("scm*", "aliexpress.*", "AliExpress tracking"),
        P("pvid", "aliexpress.*", "AliExpress tracking"),
        P("algo_*", "aliexpress.*", "AliExpress tracking"),
        P("ws_ab_test", "aliexpress.*", "AliExpress tracking"),
        P("btsid", "aliexpress.*", "AliExpress tracking"),
        P("gps-id", "aliexpress.*", "AliExpress tracking"),
        P("cv", "aliexpress.*", "AliExpress tracking"),
        P("af", "aliexpress.*", "AliExpress tracking"),
        P("dp", "aliexpress.*", "AliExpress tracking"),
        P("sk", "aliexpress.*", "AliExpress share tracking"),
        P("mall_affr", "aliexpress.*", "AliExpress affiliate tracking"),
        P("terminal_id", "aliexpress.*", "AliExpress tracking - which device"),
        P("aff_*", "aliexpress.*", "AliExpress affiliate tracking"),
        P("afSmartRedirect", "aliexpress.*", "AliExpress affiliate tracking"),
        P("srcSns", "aliexpress.*", "AliExpress share tracking"),
        P("spreadType", "aliexpress.*", "AliExpress share tracking"),
        P("bizType", "aliexpress.*", "AliExpress share tracking"),
        P("social_params", "aliexpress.*", "AliExpress share tracking"),
        P("pdp_npi", "aliexpress.*", "AliExpress tracking - the price you saw"),
        P("pdp_ext_f", "aliexpress.*", "AliExpress tracking"),
        P("gatewayAdapt", "aliexpress.*", "AliExpress tracking"),
        P("bi_*", "allegro.*", "Allegro ad and listing tracking"),
        P("reco_id", "allegro.*", "Allegro recommendation tracking"),
        P("sid", "allegro.*", "Allegro session tracking"),
        P("emission_unit_id", "allegro.*", "Allegro ad tracking"),
        P("emission_id", "allegro.*", "Allegro ad tracking"),
        P("_x_*", "temu.*", "Temu tracking"),
        P("refer_page_*", "temu.*", "Temu tracking - where you came from"),
        P("share_uin", "temu.*", "Temu share tracking - who shared it"),
        P("_bg_fs", "temu.*", "Temu tracking"),
        P("_oak_*", "temu.*", "Temu tracking"),
        P("_p_rfs", "temu.*", "Temu tracking"),
        P("src_module", "shein.*", "Shein tracking"),
        P("src_identifier", "shein.*", "Shein tracking"),
        P("src_tab_page_id", "shein.*", "Shein tracking"),
        P("url_from", "shein.*", "Shein share tracking"),
        P("click_key", "etsy.com", "Etsy tracking"),
        P("click_sum", "etsy.com", "Etsy tracking"),
        P("organic_search_click", "etsy.com", "Etsy tracking"),
        P("ref", "etsy.com", "Etsy referral"),
        P("ga_*", "etsy.com", "Etsy tracking"),
        P("u1", "walmart.*", "Walmart tracking"),
        P("ath*", "walmart.*", "Walmart ad tracking"),
        P("tag", "ceneo.pl", "Ceneo tracking"),
    };

    public static readonly List<Redirect> Redirects = new List<Redirect> {
        R("google.*", "/url", "q url", "Google search results and Gmail"),
        R("googleadservices.com", "/pagead/aclk", "adurl", "Google Ads"),
        R("safelinks.protection.outlook.com", "/", "url", "Outlook Safe Links"),
        R("statics.teams.cdn.office.net", "/evergreen-assets/safelinks/", "url", "Microsoft Teams Safe Links"),
        R("l.facebook.com", "/l.php", "u", "Facebook"),
        R("lm.facebook.com", "/l.php", "u", "Facebook (mobile)"),
        R("l.messenger.com", "/l.php", "u", "Messenger"),
        R("l.instagram.com", "/", "u", "Instagram"),
        R("youtube.com", "/redirect", "q", "YouTube descriptions and comments"),
        R("steamcommunity.com", "/linkfilter/", "u url", "Steam"),
        R("linkedin.com", "/safety/go", "url", "LinkedIn"),
        R("duckduckgo.com", "/l/", "uddg", "DuckDuckGo"),
        R("vk.com", "/away.php", "to", "VK"),
        R("slack-redir.net", "/link", "url", "Slack"),
        R("out.reddit.com", "/", "url", "Reddit"),
    };

    public static IEnumerable<Part> Parts()
    {
        return BuiltIn.Concat(Config.CleanAdded.Select(line =>
        {
            var bits = line.Split('|');
            return new Part { Name = bits[0].Trim(), Sites = bits.Length > 1 ? bits[1].Trim() : "", What = "Added by you", Custom = true };
        }));
    }

    public static bool IsOn(Part p) { return !Config.CleanOff.Contains(p.Id, StringComparer.OrdinalIgnoreCase); }
    public static bool IsOn(Redirect r) { return !Config.UnwrapOff.Contains(r.Id, StringComparer.OrdinalIgnoreCase); }

    // The link as it will be opened, and what was done to it ("skipped Google redirect; removed
    // utm_source, fbclid"), or "" if nothing. Anything that is not an http(s) link - a file opened
    // from File Explorer - is left exactly as it is.
    public static string Apply(string url, out string changes)
    {
        changes = "";
        if (url == null || !(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                              url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))) return url;
        var said = new List<string>();
        try
        {
            // a middleman can wrap another (Outlook Safe Links around a Google link): unwrap in turns
            for (int i = 0; i < 5 && Config.UnwrapOn; i++)
            {
                string what, inner = Unwrap(url, out what);
                if (inner == null) break;
                said.Add("skipped " + what + " redirect");
                url = inner;
            }
            if (Config.CleanOn)
            {
                var removed = new List<string>();
                url = RemoveTracking(url, removed);
                if (removed.Count > 0) said.Add("removed " + string.Join(", ", removed.Distinct()));
            }
        }
        catch { }   // a link this cannot read is opened as it came
        changes = string.Join("; ", said);
        return url;
    }

    // The link inside a middleman's link, or null if it is not one (or holds nothing usable).
    static string Unwrap(string url, out string what)
    {
        what = null;
        Uri u;
        if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return null;
        string host = u.Host.ToLowerInvariant(), path = u.AbsolutePath;
        foreach (var r in Redirects)
        {
            if (!IsOn(r) || !OnSite(host, r.Host)) continue;
            if (r.Path == "/" ? false : !path.StartsWith(r.Path, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (string name in r.Param.Split(' '))
            {
                string value = QueryValue(u.Query, name);
                if (value == null) continue;
                value = Uri.UnescapeDataString(value).Trim();
                // only ever a real web address - never a script or a file hidden in the parameter
                if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    Uri check;
                    if (!Uri.TryCreate(value, UriKind.Absolute, out check)) continue;
                    what = r.What.Split(' ')[0].TrimEnd(',');
                    return value;
                }
            }
        }
        return null;
    }

    static string QueryValue(string query, string name)
    {
        foreach (string pair in query.TrimStart('?').Split('&'))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0 && string.Equals(pair.Substring(0, eq), name, StringComparison.OrdinalIgnoreCase))
                return pair.Substring(eq + 1);
        }
        return null;
    }

    // Takes the tracking parts out of the query (after the ?), leaving everything else - the other
    // parts, their order and their exact spelling, and anything after a # - untouched. Amazon also
    // writes its tracking into the path itself, "/ref=sr_1_3", which goes too.
    static string RemoveTracking(string url, List<string> removed)
    {
        string fragment = "";
        int hash = url.IndexOf('#');
        if (hash >= 0) { fragment = url.Substring(hash); url = url.Substring(0, hash); }
        string query = "";
        int q = url.IndexOf('?');
        if (q >= 0) { query = url.Substring(q + 1); url = url.Substring(0, q); }

        Uri u;
        string host = Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Host.ToLowerInvariant() : "";
        var parts = Parts().Where(p => IsOn(p) && (p.Sites.Length == 0 || p.Sites.Split(' ').Any(s => OnSite(host, s)))).ToList();

        if (host.Length > 0 && OnSite(host, "amazon.*") && parts.Any(p => p.Name == "ref_"))
        {
            int at = url.IndexOf("/ref=", StringComparison.OrdinalIgnoreCase);
            if (at > url.IndexOf("//", StringComparison.Ordinal) + 1) { url = url.Substring(0, at); removed.Add("/ref="); }
        }

        if (query.Length > 0)
        {
            var kept = new List<string>();
            foreach (string pair in query.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                string name = Uri.UnescapeDataString(eq >= 0 ? pair.Substring(0, eq) : pair);
                var hit = parts.FirstOrDefault(p => Matches(p.Name, name));
                if (hit != null) removed.Add(name);
                else kept.Add(pair);
            }
            if (kept.Count > 0) url += "?" + string.Join("&", kept);
        }
        return url + fragment;
    }

    static bool Matches(string pattern, string name)
    {
        return pattern.EndsWith("*") ? name.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)
                                     : string.Equals(pattern, name, StringComparison.OrdinalIgnoreCase);
    }

    // "youtube.com" is youtube.com and every site under it (www., m., music.); "amazon.*" is Amazon
    // in every country (amazon.de, amazon.co.uk, www.amazon.pl).
    public static bool OnSite(string host, string site)
    {
        if (site.EndsWith(".*"))
            return ("." + host + ".").Contains("." + site.Substring(0, site.Length - 2) + ".");
        return host == site || host.EndsWith("." + site);
    }
}

// Copied links, cleaned: while it is turned on, the dock is told each time something is copied. If
// what was copied is one link and nothing else - YouTube's "Copy link" with its ?si=... - the same
// cleaning is done to it and the clean link is put back, so pasting gives the clean one.
//
// Anything else is left exactly as it was: text with a link inside, several lines, and whatever a
// password manager copies (it marks that as private). The copied text is only looked at in memory
// and never kept - except the link itself in the link log, if that is on. Windows' own clipboard
// history (Win+V) keeps the link as it was copied too: it takes it before any program can clean it.
class CopiedLinks : NativeWindow, IDisposable
{
    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    const int WM_CLIPBOARDUPDATE = 0x031D;

    // How password managers (and other careful programs) say "private - do not look, do not keep".
    static readonly uint[] Private = {
        RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"),
        RegisterClipboardFormat("Clipboard Viewer Ignore"),
        RegisterClipboardFormat("CanIncludeInClipboardHistory") };

    // A program can write the clipboard in a few quick steps; look once it has finished.
    readonly Timer settle = new Timer { Interval = 150 };
    public event Action<string> Cleaned;   // what was done, for the note on screen

    public CopiedLinks()
    {
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) });   // message-only
        AddClipboardFormatListener(Handle);
        settle.Tick += delegate { settle.Stop(); Look(); };
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_CLIPBOARDUPDATE && Config.CopyCleanOn) { settle.Stop(); settle.Start(); }
        base.WndProc(ref m);
    }

    void Look()
    {
        if (!Config.CopyCleanOn || Private.Any(IsClipboardFormatAvailable)) return;
        string text;
        try { if (!Clipboard.ContainsText()) return; text = Clipboard.GetText(); } catch { return; }   // busy: leave it
        string link = text.Trim();
        if (link.Length == 0 || link.Length > 4000 || link.Any(char.IsWhiteSpace)) return;   // not one link on its own
        string changes, clean = Cleaner.Apply(link, out changes);
        if (changes.Length == 0 || clean == link) return;   // nothing to take out - and the clean one, put back, stops here
        string from = CopiedFrom();
        try { Clipboard.SetDataObject(text.Replace(link, clean), true, 5, 50); } catch { return; }
        LinkLog.Add(new LinkLog.Entry { When = DateTime.Now, From = from ?? "", OpenedIn = "(copied)", Why = "the clipboard - copied, not opened",
                                        Asked = link, Opened = clean, Changes = changes });
        if (Cleaned != null) Cleaned(changes);
    }

    // The program that copied it, "Signal.exe" - or the one in front, if the clipboard does not say.
    static string CopiedFrom()
    {
        try
        {
            IntPtr w = GetClipboardOwner();
            if (w == IntPtr.Zero) w = GetForegroundWindow();
            uint pid;
            if (w == IntPtr.Zero || GetWindowThreadProcessId(w, out pid) == 0) return null;
            using (var p = System.Diagnostics.Process.GetProcessById((int)pid)) return p.ProcessName + ".exe";
        }
        catch { return null; }
    }

    public void Dispose() { settle.Dispose(); RemoveClipboardFormatListener(Handle); DestroyHandle(); }
}

// The Link cleaning tab: both switches, every tracking part and middleman with its own tick, parts of
// your own, and a box to try a link and see what it becomes.
class CleaningPage : UserControl
{
    readonly Action save;
    readonly ListView parts = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, Dock = DockStyle.Fill,
                                             HeaderStyle = ColumnHeaderStyle.Nonclickable, HideSelection = false };
    readonly ListView redirects = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, Dock = DockStyle.Fill,
                                                 HeaderStyle = ColumnHeaderStyle.Nonclickable };
    readonly TextBox tryBox = new TextBox { Dock = DockStyle.Fill };
    readonly TextBox tryResult = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = SystemColors.Control };
    readonly Label tryChanges = new Label { Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText };
    bool filling;

    public CleaningPage(Action save)
    {
        this.save = save;
        Font = new Font("Segoe UI", 9F);
        Dock = DockStyle.Fill;
        Padding = new Padding(8, 6, 8, 6);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        var clean = new CheckBox { Text = "Remove tracking from links", AutoSize = true, Checked = Config.CleanOn, Font = new Font(Font, FontStyle.Bold) };
        clean.CheckedChanged += delegate { Config.CleanOn = clean.Checked; save(); TryIt(); };
        var unwrap = new CheckBox { Text = "Skip redirects", AutoSize = true, Checked = Config.UnwrapOn, Font = new Font(Font, FontStyle.Bold),
                                    Margin = new Padding(24, 3, 3, 3) };
        unwrap.CheckedChanged += delegate { Config.UnwrapOn = unwrap.Checked; save(); TryIt(); };
        top.Controls.Add(clean);
        top.Controls.Add(unwrap);
        var copied = new CheckBox { Text = "Clean copied links too", AutoSize = true, Checked = Config.CopyCleanOn, Font = new Font(Font, FontStyle.Bold),
                                    Margin = new Padding(24, 3, 3, 3) };
        copied.CheckedChanged += delegate { Config.CopyCleanOn = copied.Checked; save(); };
        top.Controls.Add(copied);
        top.Controls.Add(new TabHelp("The Link cleaning tab",
            "# Remove tracking",
            "What goes: utm_source, fbclid, si, shops' tracking - the page is the same without them. Only known parts go.",
            "# Skip redirects",
            "Middlemen: google.com/url?q=… and others - the real link inside opens directly.",
            "# Clean copied links too",
            "When you copy: a link on its own is cleaned at once, so you paste it clean.",
            "Left alone: text with a link inside, and what password managers copy. Win+V still shows the original.",
            "# The two lists",
            "Untick one: it stays in links. utm_* is every part starting utm_. Add a part… adds your own.",
            "# Not possible",
            "Links clicked inside a browser never reach LinkPilot. Short links (bit.ly) are not followed - it stays offline.")
            { Margin = new Padding(10, 4, 0, 0) });

        // the two lists, side by side
        var lists = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

        parts.Columns.Add("Tracking part", 100);
        parts.Columns.Add("On", 115);
        parts.Columns.Add("What it is", 200);
        parts.Resize += delegate { FitLastColumn(parts); };
        parts.ItemChecked += (s, e) => { if (!filling) { Toggle(Config.CleanOff, ((Cleaner.Part)e.Item.Tag).Id, e.Item.Checked); save(); TryIt(); } };
        var partButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(0, 3, 0, 0) };
        var add = new Button { Text = "Add a part…", AutoSize = true };
        add.Click += delegate { AddPart(); };
        var remove = new Button { Text = "Remove", AutoSize = true };
        remove.Click += delegate { RemovePart(); };
        partButtons.Controls.Add(add);
        partButtons.Controls.Add(remove);
        partButtons.Controls.Add(new Label { Text = "(only parts you added)", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 8, 0, 0) });
        var left = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
        left.Controls.Add(parts);
        left.Controls.Add(Ui.Section("Tracking parts removed"));
        left.Controls.Add(partButtons);

        redirects.Columns.Add("Redirect", 165);
        redirects.Columns.Add("Used by", 150);
        redirects.Resize += delegate { FitLastColumn(redirects); };
        redirects.ItemChecked += (s, e) => { if (!filling) { Toggle(Config.UnwrapOff, ((Cleaner.Redirect)e.Item.Tag).Id, e.Item.Checked); save(); TryIt(); } };
        var right = new Panel { Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        right.Controls.Add(redirects);
        right.Controls.Add(Ui.Section("Redirects skipped"));

        lists.Controls.Add(left, 0, 0);
        lists.Controls.Add(right, 1, 0);

        // try a link
        var tryPanel = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 92, ColumnCount = 2, Padding = new Padding(0, 8, 0, 0) };
        tryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        tryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tryPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        tryPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        tryPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        tryPanel.Controls.Add(new Label { Text = "Try a link:", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 5, 0, 0) }, 0, 0);
        tryPanel.Controls.Add(tryBox, 1, 0);
        tryPanel.Controls.Add(new Label { Text = "It becomes:", AutoSize = true, Margin = new Padding(0, 4, 0, 0) }, 0, 1);
        tryPanel.Controls.Add(tryResult, 1, 1);
        tryPanel.Controls.Add(tryChanges, 1, 2);
        tryBox.TextChanged += delegate { TryIt(); };
        tryBox.Text = "https://www.google.com/url?q=https://www.youtube.com/watch%3Fv%3DdQw4w9WgXcQ%26si%3DxYz123&sa=D&utm_source=chat";

        Controls.Add(lists);
        Controls.Add(top);
        Controls.Add(tryPanel);
        Fill();
    }

    void Fill()
    {
        filling = true;
        parts.BeginUpdate();
        parts.Items.Clear();
        foreach (var p in Cleaner.Parts())
        {
            var item = new ListViewItem(p.Name) { Tag = p, Checked = Cleaner.IsOn(p) };
            item.SubItems.Add(p.Sites.Length == 0 ? "every site" : p.Sites.Replace(" ", ", ").Replace(".*", ""));
            item.SubItems.Add(p.What);
            parts.Items.Add(item);
        }
        parts.EndUpdate();
        redirects.BeginUpdate();
        redirects.Items.Clear();
        foreach (var r in Cleaner.Redirects)
        {
            var item = new ListViewItem(r.Host.Replace(".*", "") + r.Path.TrimEnd('/')) { Tag = r, Checked = Cleaner.IsOn(r) };
            item.SubItems.Add(r.What);
            redirects.Items.Add(item);
        }
        redirects.EndUpdate();
        filling = false;
        TryIt();
    }

    // The last column takes whatever width is left, so there is no sideways scrolling; longer text
    // ends in "..." instead.
    static void FitLastColumn(ListView list)
    {
        int others = 0;
        for (int i = 0; i < list.Columns.Count - 1; i++) others += list.Columns[i].Width;
        list.Columns[list.Columns.Count - 1].Width = Math.Max(60, list.ClientSize.Width - others - 1);
    }

    static void Toggle(List<string> off, string id, bool on)
    {
        off.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        if (!on) off.Add(id);
    }

    void TryIt()
    {
        string changes, result = Cleaner.Apply(tryBox.Text.Trim(), out changes);
        tryResult.Text = result;
        tryChanges.Text = tryBox.Text.Trim().Length == 0 ? "" : changes.Length > 0 ? changes : "nothing to change";
    }

    void AddPart()
    {
        using (var d = new Form { Text = "Add a tracking part", Size = new Size(440, 230), FormBorderStyle = FormBorderStyle.FixedDialog,
                                  StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false,
                                  ShowInTaskbar = false, Font = Font })
        {
            var name = new TextBox { Left = 150, Top = 16, Width = 260 };
            var sites = new TextBox { Left = 150, Top = 50, Width = 260 };
            var hint = new Label { Left = 14, Top = 84, Width = 400, Height = 50, ForeColor = SystemColors.GrayText,
                Text = "The name is what comes before = in the link, e.g. ref in ...?ref=newsletter. End it with * for every " +
                       "name that starts the same (utm_*). Leave the sites empty for every site." };
            var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, Left = 244, Top = 144, Width = 80 };
            var no = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 330, Top = 144, Width = 80 };
            d.Controls.AddRange(new Control[] {
                new Label { Text = "Name:", Left = 14, Top = 19, AutoSize = true }, name,
                new Label { Text = "Only on (optional):", Left = 14, Top = 53, AutoSize = true }, sites, hint, ok, no });
            d.AcceptButton = ok; d.CancelButton = no;
            Ui.HandCursors(d);
            if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
            string n = name.Text.Trim().Replace("|", "").Replace("=", "");
            if (n.Length == 0) return;
            string where = string.Join(" ", sites.Text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                           .Select(x => Router.CleanAddress(x).ToLowerInvariant()));
            Config.CleanAdded.Add(n + "|" + where.Replace("|", ""));
            save();
            Fill();
        }
    }

    void RemovePart()
    {
        if (parts.SelectedItems.Count == 0) return;
        var p = (Cleaner.Part)parts.SelectedItems[0].Tag;
        if (!p.Custom) { MessageBox.Show(FindForm(), "Built-in parts cannot be removed - untick it instead.", "LinkPilot"); return; }
        Config.CleanAdded.RemoveAll(x => string.Equals(x, p.Name + "|" + p.Sites, StringComparison.OrdinalIgnoreCase));
        Config.CleanOff.RemoveAll(x => string.Equals(x, p.Id, StringComparison.OrdinalIgnoreCase));
        save();
        Fill();
    }
}
