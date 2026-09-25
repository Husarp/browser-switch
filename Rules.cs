// Link rules: send a link to a category by the app it came from (Signal -> Work) or by its address
// (github.com -> Home). Rules win over the live category: the first rule that matches decides, and a
// link no rule matches goes to the live category as before.
//
// The app is known because Windows starts LinkPilot from inside the program that opened the
// link, so that program is LinkPilot's parent process. A few programs - mostly Store apps - hand
// links over through a Windows go-between, and then the go-between is all that can be seen.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Router
{
    static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }

    // The rule a link falls under, or null. A rule whose category is gone or has no browser is passed
    // over, so a link is never sent nowhere.
    public static Rule Decide(string url, string source)
    {
        if (!Config.RulesOn) return null;
        foreach (var r in Config.Rules)
        {
            if (!r.On) continue;
            var c = Config.Categories.FirstOrDefault(x => Same(x.Name, r.Category));
            if (c == null || c.Exe.Length == 0 || !File.Exists(c.Exe)) continue;
            if (r.ByApp ? FromApp(r, source) : HasAddress(r.Match, url)) return r;
        }
        return null;
    }

    static bool FromApp(Rule r, string source)
    {
        return !string.IsNullOrEmpty(source) && r.Match.Split(';').Any(m => Same(m.Trim(), source));
    }

    // "github.com" matches github.com and any site under it (gist.github.com, www.github.com). Text
    // with a "/" in it is looked for anywhere in the address ("github.com/my-company").
    public static bool HasAddress(string pattern, string url)
    {
        string p = (pattern ?? "").Trim().ToLowerInvariant();
        if (p.Length == 0 || string.IsNullOrEmpty(url)) return false;
        string u = url.ToLowerInvariant();
        if (p.Contains("/")) return u.Contains(p);
        if (p.StartsWith("*.")) p = p.Substring(2);
        Uri parsed;
        if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) || string.IsNullOrEmpty(parsed.Host)) return u.Contains(p);
        string host = parsed.Host.ToLowerInvariant();
        return host == p || host.EndsWith("." + p);
    }

    // What someone typed for an address, as a rule matches it: "https://github.com/" -> "github.com".
    public static string CleanAddress(string text)
    {
        string t = (text ?? "").Trim();
        foreach (string scheme in new[] { "https://", "http://" })
            if (t.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) t = t.Substring(scheme.Length);
        return t.TrimEnd('/');
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }
    [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] static extern bool Process32First(IntPtr snap, ref PROCESSENTRY32 e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] static extern bool Process32Next(IntPtr snap, ref PROCESSENTRY32 e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    // The program that asked Windows to open the link - this process's parent - as its file name,
    // "Signal.exe". Null if it cannot be told.
    public static string SourceApp()
    {
        IntPtr snap = CreateToolhelp32Snapshot(2, 0);   // every process
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return null;
        try
        {
            var all = new Dictionary<uint, PROCESSENTRY32>();
            var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
            if (Process32First(snap, ref e))
                do { all[e.th32ProcessID] = e; } while (Process32Next(snap, ref e));
            PROCESSENTRY32 me, parent;
            uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            if (all.TryGetValue(self, out me) && all.TryGetValue(me.th32ParentProcessID, out parent)) return parent.szExeFile;
        }
        catch { }
        finally { CloseHandle(snap); }
        return null;
    }

    static string RecentFile { get { return Path.Combine(Config.Dir, "recent-apps.txt"); } }

    // Which programs have opened links, newest first - offered in the app list as "Opened links
    // lately", so an app the built-in list does not know can still be added with one click.
    public static void Remember(string source)
    {
        if (string.IsNullOrEmpty(source) || Same(source, "BrowserSwitch.exe")) return;
        try
        {
            var list = Recent();
            list.RemoveAll(x => Same(x, source));
            list.Insert(0, source);
            File.WriteAllLines(RecentFile, list.Take(40).ToArray());
        }
        catch { }
    }

    public static List<string> Recent()
    {
        try { return File.ReadAllLines(RecentFile).Select(x => x.Trim()).Where(x => x.Length > 0).ToList(); }
        catch { return new List<string>(); }
    }
}

// Popular apps and the program files they run as, so rules can be set up before an app has ever
// opened a link. File names are those of each app's usual Windows install; if one does not match,
// "Opened links lately" in the app list shows what really opened the link.
static class AppCatalog
{
    public class App { public string Name, Group, Exes; }

    public static readonly string[] Groups = {
        "Chat & social", "Work & office", "Email", "AI assistants", "Notes & study", "Gaming",
        "Development", "Music & video", "Creative", "Files & sync", "Utilities" };

    static App A(int group, string name, string exes) { return new App { Group = Groups[group], Name = name, Exes = exes }; }

    public static readonly List<App> All = new List<App> {
        // chat & social
        A(0, "Signal", "Signal.exe"), A(0, "Discord", "Discord.exe;DiscordPTB.exe;DiscordCanary.exe"),
        A(0, "Telegram", "Telegram.exe"), A(0, "WhatsApp", "WhatsApp.exe;WhatsApp.Root.exe"),
        A(0, "Slack", "slack.exe"), A(0, "Microsoft Teams", "ms-teams.exe;Teams.exe"), A(0, "Skype", "Skype.exe"),
        A(0, "Messenger", "Messenger.exe"), A(0, "Zoom", "Zoom.exe"), A(0, "Element", "Element.exe"),
        A(0, "Viber", "Viber.exe"), A(0, "Wire", "Wire.exe"), A(0, "Session", "Session.exe"), A(0, "Beeper", "Beeper.exe"),
        A(0, "Mattermost", "Mattermost.exe"), A(0, "Rocket.Chat", "Rocket.Chat.exe"), A(0, "Guilded", "Guilded.exe"),
        A(0, "TeamSpeak", "TeamSpeak.exe;ts3client_win64.exe"), A(0, "Mumble", "mumble.exe"), A(0, "LINE", "LINE.exe"),
        A(0, "WeChat", "WeChat.exe"), A(0, "KakaoTalk", "KakaoTalk.exe"), A(0, "Pidgin", "pidgin.exe"),
        A(0, "Ferdium", "Ferdium.exe"), A(0, "Franz", "Franz.exe"), A(0, "Rambox", "Rambox.exe"),
        // work & office
        A(1, "Microsoft Word", "WINWORD.EXE"), A(1, "Microsoft Excel", "EXCEL.EXE"), A(1, "Microsoft PowerPoint", "POWERPNT.EXE"),
        A(1, "Microsoft OneNote", "ONENOTE.EXE"), A(1, "Microsoft Access", "MSACCESS.EXE"), A(1, "Microsoft Visio", "VISIO.EXE"),
        A(1, "Microsoft Project", "WINPROJ.EXE"), A(1, "Microsoft Publisher", "MSPUB.EXE"),
        A(1, "LibreOffice", "soffice.bin;soffice.exe"), A(1, "OnlyOffice", "DesktopEditors.exe"), A(1, "WPS Office", "wps.exe;et.exe;wpp.exe"),
        A(1, "Adobe Acrobat", "Acrobat.exe;AcroRd32.exe"), A(1, "Foxit PDF Reader", "FoxitPDFReader.exe"),
        A(1, "SumatraPDF", "SumatraPDF.exe"), A(1, "Notion", "Notion.exe"), A(1, "Trello", "Trello.exe"),
        A(1, "Asana", "Asana.exe"), A(1, "ClickUp", "ClickUp.exe"), A(1, "Linear", "Linear.exe"), A(1, "Miro", "Miro.exe"),
        A(1, "Todoist", "Todoist.exe"), A(1, "TickTick", "TickTick.exe"), A(1, "Microsoft To Do", "Todo.exe"),
        A(1, "TeamViewer", "TeamViewer.exe"), A(1, "AnyDesk", "AnyDesk.exe"), A(1, "Remote Desktop", "mstsc.exe"),
        // email
        A(2, "Outlook", "OUTLOOK.EXE"), A(2, "Outlook (new)", "olk.exe"), A(2, "Thunderbird", "thunderbird.exe"),
        A(2, "Windows Mail", "HxOutlook.exe"), A(2, "Mailspring", "Mailspring.exe"), A(2, "eM Client", "MailClient.exe"),
        A(2, "Mailbird", "Mailbird.exe"), A(2, "Proton Mail", "Proton Mail.exe"), A(2, "Meru", "Meru.exe"), A(2, "Postbox", "postbox.exe"),
        // AI assistants
        A(3, "Claude", "claude.exe"), A(3, "ChatGPT", "ChatGPT.exe"), A(3, "Perplexity", "Perplexity.exe"),
        A(3, "LM Studio", "LM Studio.exe"), A(3, "Ollama", "ollama app.exe"), A(3, "Jan", "Jan.exe"),
        // notes & study
        A(4, "Obsidian", "Obsidian.exe"), A(4, "Evernote", "Evernote.exe"), A(4, "Logseq", "Logseq.exe"),
        A(4, "Joplin", "Joplin.exe"), A(4, "Anki", "anki.exe"), A(4, "Zotero", "zotero.exe"), A(4, "Calibre", "calibre.exe;ebook-viewer.exe"),
        A(4, "Kindle", "Kindle.exe"), A(4, "Standard Notes", "Standard Notes.exe"), A(4, "Simplenote", "Simplenote.exe"),
        A(4, "Typora", "Typora.exe"), A(4, "Mendeley", "Mendeley Reference Manager.exe"),
        // gaming
        A(5, "Steam", "steam.exe;steamwebhelper.exe"), A(5, "Epic Games Launcher", "EpicGamesLauncher.exe"),
        A(5, "GOG Galaxy", "GalaxyClient.exe"), A(5, "Battle.net", "Battle.net.exe"), A(5, "EA app", "EADesktop.exe"),
        A(5, "Ubisoft Connect", "UbisoftConnect.exe;upc.exe"), A(5, "Riot Client", "RiotClientServices.exe;RiotClientUx.exe"),
        A(5, "League of Legends", "LeagueClientUx.exe"), A(5, "Xbox app", "XboxPcApp.exe"), A(5, "Minecraft Launcher", "MinecraftLauncher.exe"),
        A(5, "Prism Launcher", "prismlauncher.exe"), A(5, "CurseForge", "CurseForge.exe"), A(5, "Overwolf", "Overwolf.exe"),
        A(5, "osu!", "osu!.exe"), A(5, "Wargaming Game Center", "wgc.exe"), A(5, "Playnite", "Playnite.DesktopApp.exe;Playnite.FullscreenApp.exe"),
        A(5, "Heroic Games Launcher", "Heroic.exe"), A(5, "itch", "itch.exe"), A(5, "Amazon Games", "Amazon Games UI.exe"),
        A(5, "Medal", "Medal.exe"), A(5, "Parsec", "parsecd.exe"), A(5, "Rockstar Games Launcher", "RockstarLauncher.exe"),
        // development
        A(6, "Visual Studio Code", "Code.exe"), A(6, "Visual Studio", "devenv.exe"), A(6, "Cursor", "Cursor.exe"),
        A(6, "Windsurf", "Windsurf.exe"), A(6, "Zed", "zed.exe"), A(6, "Sublime Text", "sublime_text.exe"),
        A(6, "Notepad++", "notepad++.exe"), A(6, "JetBrains IDEs",
            "idea64.exe;pycharm64.exe;rider64.exe;webstorm64.exe;clion64.exe;goland64.exe;phpstorm64.exe;datagrip64.exe;rustrover64.exe;rubymine64.exe"),
        A(6, "Android Studio", "studio64.exe"), A(6, "GitHub Desktop", "GitHubDesktop.exe"), A(6, "GitKraken", "gitkraken.exe"),
        A(6, "Fork", "Fork.exe"), A(6, "Sourcetree", "SourceTree.exe"), A(6, "Postman", "Postman.exe"), A(6, "Insomnia", "Insomnia.exe"),
        A(6, "Bruno", "Bruno.exe"), A(6, "Docker Desktop", "Docker Desktop.exe"), A(6, "Windows Terminal", "WindowsTerminal.exe"),
        A(6, "Unity Hub", "Unity Hub.exe"), A(6, "Unity", "Unity.exe"), A(6, "DBeaver", "dbeaver.exe"),
        // music & video
        A(7, "Spotify", "Spotify.exe"), A(7, "VLC", "vlc.exe"), A(7, "Apple Music", "AppleMusic.exe"), A(7, "iTunes", "iTunes.exe"),
        A(7, "TIDAL", "TIDAL.exe"), A(7, "Deezer", "Deezer.exe"), A(7, "foobar2000", "foobar2000.exe"), A(7, "MusicBee", "MusicBee.exe"),
        A(7, "Plex", "Plex.exe"), A(7, "Jellyfin Media Player", "JellyfinMediaPlayer.exe"), A(7, "Stremio", "stremio.exe"),
        A(7, "MPC-HC", "mpc-hc64.exe;mpc-hc.exe"), A(7, "PotPlayer", "PotPlayerMini64.exe"), A(7, "OBS Studio", "obs64.exe"),
        A(7, "Streamlabs", "Streamlabs OBS.exe"), A(7, "Podcasts / Apple TV", "AppleTV.exe"),
        // creative
        A(8, "Figma", "Figma.exe"), A(8, "Photoshop", "Photoshop.exe"), A(8, "Illustrator", "Illustrator.exe"),
        A(8, "Premiere Pro", "Adobe Premiere Pro.exe"), A(8, "After Effects", "AfterFX.exe"), A(8, "Lightroom", "Lightroom.exe"),
        A(8, "Blender", "blender.exe"), A(8, "GIMP", "gimp-2.10.exe;gimp-3.0.exe;gimp.exe"), A(8, "Krita", "krita.exe"),
        A(8, "Inkscape", "inkscape.exe"), A(8, "DaVinci Resolve", "Resolve.exe"), A(8, "Canva", "Canva.exe"),
        A(8, "Paint.NET", "paintdotnet.exe"), A(8, "Audacity", "Audacity.exe"), A(8, "Adobe Creative Cloud", "Creative Cloud.exe"),
        // files & sync
        A(9, "File Explorer", "explorer.exe"), A(9, "OneDrive", "OneDrive.exe"), A(9, "Dropbox", "Dropbox.exe"),
        A(9, "Google Drive", "GoogleDriveFS.exe"), A(9, "Nextcloud", "nextcloud.exe"), A(9, "Synology Drive", "cloud-drive-ui.exe"),
        A(9, "qBittorrent", "qbittorrent.exe"), A(9, "7-Zip", "7zFM.exe"), A(9, "WinRAR", "WinRAR.exe"), A(9, "Total Commander", "TOTALCMD64.EXE"),
        // utilities
        A(10, "KeePassXC", "KeePassXC.exe"), A(10, "KeePass", "KeePass.exe"), A(10, "Bitwarden", "Bitwarden.exe"),
        A(10, "1Password", "1Password.exe"), A(10, "PowerToys Run", "PowerToys.PowerLauncher.exe"), A(10, "Flow Launcher", "Flow.Launcher.exe"),
        A(10, "Everything", "Everything.exe"), A(10, "ShareX", "ShareX.exe"), A(10, "Greenshot", "Greenshot.exe"),
        A(10, "Notepad", "Notepad.exe"), A(10, "Windows Settings", "SystemSettings.exe"), A(10, "Proton VPN", "ProtonVPN.exe"),
    };

    public static App ForExe(string exe)
    {
        return All.FirstOrDefault(a => a.Exes.Split(';').Any(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase)));
    }
}

// The Rules tab of the main window: tick a rule on or off, move it up or down (the first match
// decides), edit where it sends links, add apps or addresses. Every change is saved at once.
class RulesPage : UserControl
{
    readonly Action save;
    readonly ListView list = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false,
                                            Dock = DockStyle.Fill, HeaderStyle = ColumnHeaderStyle.Nonclickable, MultiSelect = false };
    readonly Label empty = new Label { Text = "No rules yet.\nAdd apps or addresses with the buttons on the right.",
                                       TextAlign = ContentAlignment.MiddleCenter, ForeColor = SystemColors.GrayText, BackColor = SystemColors.Window };
    bool filling, doubleClick;

    static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }

    public RulesPage(Action save)
    {
        this.save = save;
        Font = new Font("Segoe UI", 9F);
        Dock = DockStyle.Fill;
        Padding = new Padding(4);

        var use = new CheckBox { Text = "Use rules", Checked = Config.RulesOn, AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        use.CheckedChanged += delegate { Config.RulesOn = use.Checked; save(); };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(10, 9, 10, 0), WrapContents = false };
        top.Controls.Add(use);
        top.Controls.Add(new TabHelp("The Rules tab",
            "# Rules",
            "Which wins: a matching rule beats the live category; the first match decides (Move up / down).",
            "No match: the link goes to the live category.",
            "Off: untick one rule, or Use rules for all - also in the dock menu and on a shortcut.",
            "Edit: where a rule sends links, or its address - or double-click the rule.",
            "Same app or address twice: you choose - replace the older rule, or keep it.",
            "# App rules",
            "Known by: the program file. Store apps may hide behind Windows - use an address rule for those.",
            "Opened links lately: in Add apps…, what really opened your links.",
            "# Address rules",
            "github.com: also covers gist.github.com. With a / it is looked for anywhere in the link.") { Margin = new Padding(6, 1, 0, 0) });

        // an app rule fills the first column, an address rule the second - one list, so the order
        // (the first match decides) stays across both kinds
        list.Columns.Add("When a link comes from", 190);
        list.Columns.Add("or its address has", 170);
        list.Columns.Add("Goes to profile", -2);           // -2: fills the rest of the width
        list.ItemChecked += (s, e) => { if (filling) return; ((Rule)e.Item.Tag).On = e.Item.Checked; save(); };
        list.ItemActivate += delegate { Edit(); };         // double-click a rule to edit it
        // ...without the double-click also ticking or unticking it, as Windows would
        list.MouseDown += (s, e) => doubleClick = e.Clicks > 1;
        list.MouseUp += delegate { doubleClick = false; };
        list.ItemCheck += (s, e) => { if (doubleClick) e.NewValue = e.CurrentValue; };
        list.Controls.Add(empty);
        list.Resize += delegate { empty.SetBounds(0, 40, list.ClientSize.Width, 60); list.Columns[2].Width = -2; };

        var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 150, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8, 4, 4, 4) };
        side.Controls.Add(SideButton("Add apps…", delegate { AddApps(); }));
        side.Controls.Add(SideButton("Add address…", delegate { AddAddress(); }));
        side.Controls.Add(SideButton("Edit…", delegate { Edit(); }));
        side.Controls.Add(SideButton("Remove", delegate { Remove(); }));
        side.Controls.Add(SideButton("Move up", delegate { MoveRule(-1); }));
        side.Controls.Add(SideButton("Move down", delegate { MoveRule(1); }));

        Controls.Add(list);
        Controls.Add(side);
        Controls.Add(top);
        Fill();
    }

    static Button SideButton(string text, EventHandler click)
    {
        var b = new Button { Text = text, Width = 132, Height = 28 };
        b.Click += click;
        return b;
    }

    Rule Selected() { return list.SelectedItems.Count > 0 ? (Rule)list.SelectedItems[0].Tag : null; }

    void Fill(int select = -1)
    {
        if (select < 0) select = list.SelectedIndices.Count > 0 ? list.SelectedIndices[0] : -1;
        filling = true;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var r in Config.Rules)
        {
            var item = new ListViewItem(r.ByApp ? r.Label : "") { Checked = r.On, Tag = r };
            item.SubItems.Add(r.ByApp ? "" : r.Label);
            bool known = Config.Categories.Any(c => Same(c.Name, r.Category));
            item.SubItems.Add(known ? r.Category : r.Category + "  (no such category - skipped)");
            list.Items.Add(item);
        }
        list.EndUpdate();
        empty.Visible = Config.Rules.Count == 0;
        if (select >= 0 && select < list.Items.Count) { list.Items[select].Selected = true; list.Items[select].EnsureVisible(); }
        filling = false;
    }

    void Remove()
    {
        var r = Selected(); if (r == null) return;
        int at = Config.Rules.IndexOf(r);
        Config.Rules.Remove(r);
        save();
        Fill(Math.Min(at, Config.Rules.Count - 1));
    }

    void MoveRule(int by)
    {
        var r = Selected(); if (r == null) return;
        int at = Config.Rules.IndexOf(r), to = at + by;
        if (to < 0 || to >= Config.Rules.Count) return;
        Config.Rules.RemoveAt(at);
        Config.Rules.Insert(to, r);
        save();
        Fill(to);
    }

    void AddApps()
    {
        using (var picker = new AppPicker())
        {
            if (picker.ShowDialog(FindForm()) != DialogResult.OK) return;
            int last = -1;
            foreach (var a in picker.Chosen)
            {
                var r = new Rule { ByApp = true, Label = a.Name, Match = a.Exes, Category = picker.Target };
                if (Place(r, null)) last = Config.Rules.IndexOf(r);
            }
            save();
            Fill(last);
        }
    }

    void AddAddress()
    {
        var r = new Rule { ByApp = false };
        if (!RuleDialog(r, "Add an address", "Add") || !Place(r, null)) return;
        save();
        Fill(Config.Rules.IndexOf(r));
    }

    void Edit()
    {
        var r = Selected(); if (r == null) return;
        var changed = new Rule { On = r.On, ByApp = r.ByApp, Label = r.Label, Match = r.Match, Category = r.Category };
        if (!RuleDialog(changed, "Edit rule", "Save") || !Place(changed, r)) return;
        save();
        Fill(Config.Rules.IndexOf(changed));
    }

    // Puts a new rule into the list - or an edited one in place of the rule it was (was). If another
    // rule is already for the same app or address, asks which one stays: the new one then takes the
    // older one's place in the order. False: the older one stays, and nothing changes.
    bool Place(Rule r, Rule was)
    {
        var older = Config.Rules.FirstOrDefault(x => x != was && x.ByApp == r.ByApp &&
            (r.ByApp ? x.Match.Split(';').Intersect(r.Match.Split(';'), StringComparer.OrdinalIgnoreCase).Any() : Same(x.Match, r.Match)));
        if (older != null && !ReplaceOlder(older, r)) return false;
        int at = was != null ? Config.Rules.IndexOf(was) : older != null ? Config.Rules.IndexOf(older) : Config.Rules.Count;
        if (was != null) Config.Rules.Remove(was);
        if (older != null) { if (Config.Rules.IndexOf(older) < at) at--; Config.Rules.Remove(older); }
        Config.Rules.Insert(Math.Min(at, Config.Rules.Count), r);
        return true;
    }

    // "Signal already has a rule" - Replace the older rule, or Keep the older rule (also Esc).
    bool ReplaceOlder(Rule older, Rule r)
    {
        string what = older.ByApp ? older.Label : "The address " + older.Label;
        using (var d = new Form { Text = "Already has a rule", Size = new Size(470, 180), FormBorderStyle = FormBorderStyle.FixedDialog,
                                  StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false,
                                  ShowInTaskbar = false, Font = Font })
        {
            var say = new Label { Left = 14, Top = 16, Width = 430, Height = 60,
                                  Text = what + " already has a rule: its links go to " + older.Category + ".\n" +
                                         (Same(older.Category, r.Category) ? "The new one sends them there too."
                                                                           : "The new one would send them to " + r.Category + ".") };
            var replace = new Button { Text = "Replace the older rule", DialogResult = DialogResult.Yes, Left = 128, Top = 90, Width = 160 };
            var keep = new Button { Text = "Keep the older rule", DialogResult = DialogResult.No, Left = 296, Top = 90, Width = 150 };
            d.Controls.AddRange(new Control[] { say, replace, keep });
            d.AcceptButton = replace; d.CancelButton = keep;
            Ui.HandCursors(d);
            return d.ShowDialog(FindForm()) == DialogResult.Yes;
        }
    }

    // Asks for a rule's address (address rules) and where its links go, and puts the answers in r.
    // An app rule shows its app; to have another app, remove the rule and add that app.
    bool RuleDialog(Rule r, string title, string okText)
    {
        using (var d = new Form { Text = title, Size = new Size(430, 210), FormBorderStyle = FormBorderStyle.FixedDialog,
                                  StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false,
                                  ShowInTaskbar = false, Font = Font })
        {
            var ask = new Label { Text = r.ByApp ? "Links that come from:" : "Links whose address has:", Left = 14, Top = 16, AutoSize = true };
            var box = new TextBox { Left = 14, Top = 40, Width = 390, Text = r.Match };
            var app = new Label { Left = 14, Top = 42, AutoSize = true, Font = new Font(Font, FontStyle.Bold),
                                  Text = r.Label + "   (" + r.Match.Replace(";", ", ") + ")" };
            var to = new Label { Text = "go to profile:", Left = 14, Top = 84, AutoSize = true };
            var cat = new ComboBox { Left = 100, Top = 80, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cat.Items.AddRange(Config.Categories.Select(c => (object)c.Name).ToArray());
            cat.SelectedItem = cat.Items.Cast<object>().FirstOrDefault(o => Same((string)o, r.Category));
            if (cat.SelectedItem == null && cat.Items.Count > 0) cat.SelectedIndex = 0;
            var ok = new Button { Text = okText, DialogResult = DialogResult.OK, Left = 238, Top = 130, Width = 80 };
            var no = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 324, Top = 130, Width = 80 };
            d.Controls.AddRange(new Control[] { ask, r.ByApp ? (Control)app : box, to, cat, ok, no });
            if (!r.ByApp)
                d.Controls.Add(new TabHelp("Links to an address",
                    "Sites under it: github.com also covers gist.github.com and www.github.com.",
                    "With a /: github.com/my-company is looked for anywhere in the link.") { Left = 164, Top = 14 });
            d.AcceptButton = ok; d.CancelButton = no;
            Ui.HandCursors(d);
            if (d.ShowDialog(FindForm()) != DialogResult.OK || cat.SelectedItem == null) return false;
            if (!r.ByApp)
            {
                string address = Router.CleanAddress(box.Text);
                if (address.Length == 0) return false;
                r.Label = r.Match = address;
            }
            r.Category = (string)cat.SelectedItem;
            return true;
        }
    }
}

// The app list: search it, or browse by group; tick as many as you like; choose where their links go.
// "Opened links lately" lists the programs that actually opened links on this PC.
class AppPicker : Form
{
    const string AllGroup = "All apps", RecentGroup = "Opened links lately";
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

    public readonly List<AppCatalog.App> Chosen = new List<AppCatalog.App>();
    public string Target;

    readonly TextBox search = new TextBox { Dock = DockStyle.Fill };
    readonly ListBox groups = new ListBox { Dock = DockStyle.Left, Width = 170, IntegralHeight = false };
    readonly ListView apps = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, Dock = DockStyle.Fill,
                                            HeaderStyle = ColumnHeaderStyle.Nonclickable };
    readonly ComboBox target = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    readonly List<AppCatalog.App> ticked = new List<AppCatalog.App>();   // kept while searching and browsing
    readonly List<AppCatalog.App> recent;
    bool filling;

    public AppPicker()
    {
        Text = "Add apps";
        Font = new Font("Segoe UI", 9F);
        Size = new Size(700, 500);
        MinimumSize = new Size(560, 380);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;

        recent = Router.Recent().Select(exe => AppCatalog.ForExe(exe) ??
                     new AppCatalog.App { Name = Path.GetFileNameWithoutExtension(exe), Group = RecentGroup, Exes = exe })
                 .Distinct().ToList();

        var searchRow = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 6, 8, 4) };
        searchRow.Controls.Add(search);
        var helpHolder = new Panel { Dock = DockStyle.Right, Width = 30 };   // keeps the (i) round, not stretched
        helpHolder.Controls.Add(new TabHelp("Adding apps",
            "Finding them: search, or pick a group on the left.",
            "Several at once: tick as many as you like - the ticks stay while you search and switch groups - then " +
                "choose where their links go and press Add.",
            "Opened links lately: the programs that really opened links on this PC, so an app missing from the list " +
                "can still be added.",
            "Not there either: use Browse for a program….") { Left = 8, Top = 1 });
        searchRow.Controls.Add(helpHolder);
        search.HandleCreated += delegate { SendMessage(search.Handle, 0x1501, (IntPtr)1, "Search apps…"); };   // grey hint text
        search.TextChanged += delegate { Fill(); };

        groups.Items.Add(AllGroup);
        groups.Items.Add(RecentGroup + (recent.Count > 0 ? "  (" + recent.Count + ")" : ""));
        groups.Items.AddRange(AppCatalog.Groups.Select(g => (object)g).ToArray());
        groups.SelectedIndex = recent.Count > 0 ? 1 : 0;
        groups.SelectedIndexChanged += delegate { Fill(); };

        apps.Columns.Add("App", 220);
        apps.Columns.Add("Program file", 250);
        apps.ItemChecked += (s, e) =>
        {
            if (filling) return;
            var a = (AppCatalog.App)e.Item.Tag;
            if (e.Item.Checked) { if (!ticked.Contains(a)) ticked.Add(a); } else ticked.Remove(a);
        };
        apps.ItemActivate += delegate { if (apps.FocusedItem != null) apps.FocusedItem.Checked = !apps.FocusedItem.Checked; };

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8, 7, 8, 4) };
        var browse = new Button { Text = "Browse for a program…", AutoSize = true };
        browse.Click += delegate { Browse(); };
        bottom.Controls.Add(browse);
        bottom.Controls.Add(new Label { Text = "Send their links to:", AutoSize = true, Margin = new Padding(24, 7, 3, 3) });
        target.Items.AddRange(Config.Categories.Select(c => (object)c.Name).ToArray());
        if (target.Items.Count > 0) target.SelectedIndex = 0;
        bottom.Controls.Add(target);
        var add = new Button { Text = "Add", AutoSize = true, Margin = new Padding(16, 3, 3, 3) };
        add.Click += delegate
        {
            if (ticked.Count == 0) { MessageBox.Show(this, "Tick at least one app first."); return; }
            if (target.SelectedItem == null) { MessageBox.Show(this, "Make a category first - links need somewhere to go."); return; }
            Chosen.AddRange(ticked);
            Target = (string)target.SelectedItem;
            DialogResult = DialogResult.OK;
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        bottom.Controls.Add(add);
        bottom.Controls.Add(cancel);
        CancelButton = cancel;

        Controls.Add(apps);
        Controls.Add(groups);
        Controls.Add(searchRow);
        Controls.Add(bottom);
        Ui.HandCursors(this);
        Fill();
    }

    // What the list shows: while searching, every app whose name or program file has the text;
    // otherwise the chosen group.
    void Fill()
    {
        string q = search.Text.Trim();
        string g = groups.SelectedItem == null ? AllGroup : ((string)groups.SelectedItem).Split(new[] { "  (" }, StringSplitOptions.None)[0];
        IEnumerable<AppCatalog.App> shown =
            q.Length > 0 ? AppCatalog.All.Concat(recent).Distinct()
                                .Where(a => a.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            a.Exes.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
            : g == RecentGroup ? recent
            : g == AllGroup ? AppCatalog.All.OrderBy(a => a.Name)
            : AppCatalog.All.Where(a => a.Group == g);
        filling = true;
        apps.BeginUpdate();
        apps.Items.Clear();
        foreach (var a in shown)
        {
            var item = new ListViewItem(a.Name) { Tag = a, Checked = ticked.Contains(a) };
            item.SubItems.Add(a.Exes.Replace(";", ", "));
            apps.Items.Add(item);
        }
        apps.EndUpdate();
        filling = false;
    }

    void Browse()
    {
        using (var d = new OpenFileDialog { Title = "The program whose links should have a rule", Filter = "Programs|*.exe" })
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            string exe = Path.GetFileName(d.FileName);
            var a = AppCatalog.ForExe(exe) ?? new AppCatalog.App { Name = Path.GetFileNameWithoutExtension(exe), Group = RecentGroup, Exes = exe };
            if (!recent.Contains(a)) recent.Insert(0, a);
            if (!ticked.Contains(a)) ticked.Add(a);
            search.Text = "";
            groups.SelectedIndex = 1;
            Fill();
        }
    }
}
