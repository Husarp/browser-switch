// Browser Switch - decides which browser, and which profile of it, every link opens in.
//
// Windows will not let a script change your default browser: the setting carries a signature tied to
// your account, and anything written directly is thrown away. That protection stops browser
// hijacking and is worth keeping. So this does not fight it. Browser Switch IS the default browser,
// and it forwards each link to the real one.
//
// You set up named categories - Work, Home, School, whatever you like - and point each at a browser
// AND one of its profiles. One click switches which category is live.
//
//   BrowserSwitch.exe <url>          open the link in the live category's browser and profile
//   BrowserSwitch.exe                open the window: pick browsers, edit categories, switch
//   BrowserSwitch.exe --tray         sit in the notification area only - what runs at sign-in
//   BrowserSwitch.exe --switch Work  make a category live without opening anything
//   BrowserSwitch.exe --dry <url>    write where the link WOULD go to dry-run.log and open nothing
//
// Nothing is assumed and nothing is hard-coded: browsers come from the Windows registry, profiles
// from each browser's own files, and every category is one you made. If anything fails, the link
// still opens in the first browser Windows lists - a broken switcher must never mean a dead link.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

// The name, publisher and version Windows reads out of the exe itself - for Settings > Default apps,
// "Open with", Task Manager. Without these the exe's description is a single blank space, so Windows
// had nothing to call this program but "BrowserSwitch.exe".
[assembly: System.Reflection.AssemblyTitle("Browser Switch")]
[assembly: System.Reflection.AssemblyProduct("Browser Switch")]
[assembly: System.Reflection.AssemblyCompany("Browser Switch")]
[assembly: System.Reflection.AssemblyDescription("Sends each link to the browser and profile you chose")]
[assembly: System.Reflection.AssemblyVersion("2.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.2.0.0")]

// ---- what we know about the machine ------------------------------------------------------------

class Profile
{
    public string Name;        // what the browser calls it: "Work", "Games"
    public string Args;        // how to launch it, already quoted
    public override string ToString() { return Name; }
}

class Browser
{
    public string Name;        // "Brave", "Mozilla Firefox"
    public string Exe;
    public List<Profile> Profiles = new List<Profile>();
    public override string ToString() { return Name; }
}

class Category
{
    public string Name;        // "Work"
    public string Exe;
    public string Args;
    public string Shows;       // "Brave - Work", for the window and the tooltip
    public bool InDock;        // has its own icon in the dock: one click switches to it
    public string Icon = "";   // that icon: "" = the browser's own, "color:#RRGGBB", "file:<path>"
    public string HotKey = ""; // keyboard shortcut that switches to it: "Ctrl+Alt+1", or ""
    public string DefaultKey = "";  // the shortcut it was suggested - what Reset puts back
    public bool InCycle = true;     // included when stepping with next / previous
    public bool KeyOn = true;       // its shortcut is in use; off keeps the key but does nothing
}

// ---- finding browsers and their profiles -------------------------------------------------------

static class Machine
{
    // Every browser that registered itself with Windows. Ourselves and Internet Explorer excluded.
    public static List<Browser> Browsers()
    {
        var found = new List<Browser>();
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using (var key = root.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet"))
            {
                if (key == null) continue;
                foreach (string sub in key.GetSubKeyNames())
                {
                    using (var b = key.OpenSubKey(sub))
                    using (var cmd = key.OpenSubKey(sub + @"\shell\open\command"))
                    {
                        if (b == null || cmd == null) continue;
                        string name = (b.GetValue(null) as string) ?? sub;
                        string exe = Unquote((cmd.GetValue(null) as string) ?? "");
                        if (exe.Length == 0 || !File.Exists(exe)) continue;
                        string file = Path.GetFileName(exe).ToLowerInvariant();
                        if (file == "browserswitch.exe" || file == "iexplore.exe") continue;
                        if (found.Any(x => string.Equals(x.Exe, exe, StringComparison.OrdinalIgnoreCase))) continue;
                        var browser = new Browser { Name = name, Exe = exe };
                        LoadProfiles(browser);
                        found.Add(browser);
                    }
                }
            }
        }
        return found.OrderBy(x => x.Name).ToList();
    }

    static string Unquote(string s)
    {
        s = s.Trim();
        if (s.StartsWith("\""))
        {
            int end = s.IndexOf('"', 1);
            if (end > 0) return s.Substring(1, end - 1);
        }
        int space = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return space > 0 ? s.Substring(0, space + 4) : s;
    }

    static void LoadProfiles(Browser b)
    {
        string exe = Path.GetFileName(b.Exe).ToLowerInvariant();
        if (exe == "firefox.exe") FirefoxProfiles(b);
        else ChromiumProfiles(b, exe);

        // Every browser can always be opened the plain way, however it was last left.
        b.Profiles.Insert(0, new Profile { Name = "(as it opens normally)", Args = "" });
    }

    // Firefox keeps a plain ini file listing its profiles.
    static void FirefoxProfiles(Browser b)
    {
        string ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                  @"Mozilla\Firefox\profiles.ini");
        if (!File.Exists(ini)) return;
        string name = null;
        foreach (string raw in File.ReadAllLines(ini))
        {
            string line = raw.Trim();
            if (line.StartsWith("["))
            {
                if (name != null) Add(b, name, "-P \"" + name + "\"");
                name = null;
            }
            else if (line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
            {
                name = line.Substring(5).Trim();
            }
        }
        if (name != null) Add(b, name, "-P \"" + name + "\"");
    }

    // Chrome, Edge, Brave, Vivaldi and the rest all keep a "Local State" file whose profile.info_cache
    // maps a folder ("Profile 2") to the name you gave it ("Work"). It is read by hand rather than
    // with a JSON parser, because these files are large and have repeated keys that parsers reject.
    // Where a Chromium-family browser keeps its profiles, from its exe's file name; null if unknown.
    static string UserData(string exe)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var places = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "chrome.exe",  Path.Combine(local, @"Google\Chrome\User Data") },
            { "msedge.exe",  Path.Combine(local, @"Microsoft\Edge\User Data") },
            { "brave.exe",   Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data") },
            { "vivaldi.exe", Path.Combine(local, @"Vivaldi\User Data") },
            { "opera.exe",   Path.Combine(roaming, @"Opera Software\Opera Stable") },
        };
        string dir;
        return places.TryGetValue(exe, out dir) ? dir : null;
    }

    // The icon a Chromium browser made for one profile - its own icon with the profile's picture
    // on it, the one the taskbar shows. Brave and Chrome call it "Google Profile.ico", Edge "Edge
    // Profile.ico". Null when the category is not a Chromium profile or the file is not there.
    public static string ProfileIcon(string browserExe, string args)
    {
        var m = System.Text.RegularExpressions.Regex.Match(args ?? "", "--profile-directory=\"([^\"]+)\"");
        string dir = UserData(Path.GetFileName(browserExe ?? ""));
        if (!m.Success || dir == null) return null;
        foreach (string name in new[] { "Google Profile.ico", "Edge Profile.ico" })
        {
            string file = Path.Combine(dir, m.Groups[1].Value, name);
            if (File.Exists(file)) return file;
        }
        return null;
    }

    static void ChromiumProfiles(Browser b, string exe)
    {
        string dir = UserData(exe);
        if (dir == null || !Directory.Exists(dir)) return;

        string state = Path.Combine(dir, "Local State");
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(state))
        {
            try
            {
                string text = File.ReadAllText(state);
                int at = text.IndexOf("\"info_cache\"", StringComparison.Ordinal);
                if (at >= 0)
                {
                    int open = text.IndexOf('{', at), depth = 0, end = open;
                    for (int i = open; i < text.Length && i < open + 400000; i++)
                    {
                        if (text[i] == '{') depth++;
                        else if (text[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
                    }
                    string block = text.Substring(open, end - open + 1);
                    // each entry looks like  "Profile 2":{ ... "name":"Work" ... }
                    foreach (System.Text.RegularExpressions.Match m in
                             System.Text.RegularExpressions.Regex.Matches(block,
                                 "\"([^\"]+)\"\\s*:\\s*\\{", System.Text.RegularExpressions.RegexOptions.None))
                    {
                        string folder = m.Groups[1].Value;
                        if (!Directory.Exists(Path.Combine(dir, folder))) continue;
                        int from = m.Index + m.Length;
                        int stop = block.IndexOf("},\"", from, StringComparison.Ordinal);
                        string entry = block.Substring(from, (stop > from ? stop : block.Length - 1) - from);
                        var nm = System.Text.RegularExpressions.Regex.Match(entry, "\"name\"\\s*:\\s*\"([^\"]*)\"");
                        names[folder] = nm.Success && nm.Groups[1].Value.Length > 0 ? Unescape(nm.Groups[1].Value) : folder;
                    }
                }
            }
            catch { }
        }

        // fall back to the folders themselves if the file could not be read
        foreach (string sub in Directory.GetDirectories(dir))
        {
            string folder = Path.GetFileName(sub);
            if (folder != "Default" && !folder.StartsWith("Profile ")) continue;
            string shown;
            if (!names.TryGetValue(folder, out shown)) shown = folder;
            Add(b, shown == folder ? folder : shown + "  (" + folder + ")",
                   "--profile-directory=\"" + folder + "\"");
        }
    }

    static string Unescape(string s) { return s.Replace("\\u0026", "&").Replace("\\\"", "\"").Replace("\\\\", "\\"); }

    static void Add(Browser b, string name, string args)
    {
        if (b.Profiles.Any(p => p.Args == args)) return;
        b.Profiles.Add(new Profile { Name = name, Args = args });
    }
}

// ---- the categories you made -------------------------------------------------------------------

static class Config
{
    public static string Dir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
    public static string File_ { get { return Path.Combine(Dir, "config.txt"); } }
    // The text last read or written, so the dock can tell its own writes from someone else's.
    public static string LastText = "";

    public static List<Category> Categories = new List<Category>();
    public static string Active = "";
    // The browser links went to before Browser Switch existed. Written at install time so that a
    // machine with nothing set up yet behaves exactly as it did before, instead of picking one.
    public static string FallbackExe = "";
    // Keyboard shortcuts: off until turned on in the window. The keys below are filled in anyway,
    // so turning them on is one tick.
    public static bool ShortcutsOn;
    public static string NextKey = "", PrevKey = "";
    public static string NextDefault = "", PrevDefault = "";   // what Reset puts back
    public static bool NextOn = true, PrevOn = true;           // each can be switched off on its own
    // What a pinned dock icon says when the mouse rests on it: "Work", or "Switch to Work".
    public static bool TipSwitchTo;

    // config.txt, one line each, so it can be read and edited by hand:
    //     active=Work
    //     shortcuts=off
    //     docktips=name            (or "switch": "Switch to Work")
    //     next=Ctrl+Alt+Space
    //     previous=Ctrl+Alt+Shift+Space
    //     default-next=Ctrl+Alt+Space              (what Reset puts back)
    //     default-previous=Ctrl+Alt+Shift+Space
    //     next-on=yes                              ("no": kept, but not in use)
    //     previous-on=yes
    //     category=Work|C:\...\brave.exe|--profile-directory="Profile 2"|Brave - Work
    //     category=Work|C:\...\brave.exe|--profile-directory="Profile 2"|Brave - Work|1|color:#6366F1|F13|Ctrl+Alt+W|no|off
    // The last six are optional: pinned to the dock ("1" or empty), the dock icon, the shortcut, the
    // shortcut it was suggested, "no" if next / previous should skip it, "off" if its shortcut is
    // switched off.
    //
    // If the file cannot be read - it is being written this very moment - everything stays as it
    // was, instead of briefly becoming "nothing set up". The dock rereads it every time it changes.
    public static bool Load()
    {
        string text;
        try { text = File.ReadAllText(File_); } catch { return false; }
        Categories.Clear(); Active = ""; FallbackExe = ""; ShortcutsOn = false; NextKey = ""; PrevKey = ""; TipSwitchTo = false;
        NextDefault = ""; PrevDefault = ""; NextOn = true; PrevOn = true;
        LastText = text;
        bool keysSeen = false, nextDefaultSeen = false, prevDefaultSeen = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            if (line.StartsWith("active=", StringComparison.OrdinalIgnoreCase)) { Active = line.Substring(7).Trim(); continue; }
            if (line.StartsWith("fallback=", StringComparison.OrdinalIgnoreCase)) { FallbackExe = line.Substring(9).Trim(); continue; }
            if (line.StartsWith("shortcuts=", StringComparison.OrdinalIgnoreCase)) { ShortcutsOn = line.Substring(10).Trim().ToLowerInvariant() == "on"; continue; }
            if (line.StartsWith("docktips=", StringComparison.OrdinalIgnoreCase)) { TipSwitchTo = line.Substring(9).Trim().ToLowerInvariant() == "switch"; continue; }
            if (line.StartsWith("next=", StringComparison.OrdinalIgnoreCase)) { NextKey = line.Substring(5).Trim(); keysSeen = true; continue; }
            if (line.StartsWith("previous=", StringComparison.OrdinalIgnoreCase)) { PrevKey = line.Substring(9).Trim(); keysSeen = true; continue; }
            if (line.StartsWith("default-next=", StringComparison.OrdinalIgnoreCase)) { NextDefault = line.Substring(13).Trim(); nextDefaultSeen = true; continue; }
            if (line.StartsWith("default-previous=", StringComparison.OrdinalIgnoreCase)) { PrevDefault = line.Substring(17).Trim(); prevDefaultSeen = true; continue; }
            if (line.StartsWith("next-on=", StringComparison.OrdinalIgnoreCase)) { NextOn = line.Substring(8).Trim().ToLowerInvariant() != "no"; continue; }
            if (line.StartsWith("previous-on=", StringComparison.OrdinalIgnoreCase)) { PrevOn = line.Substring(12).Trim().ToLowerInvariant() != "no"; continue; }
            if (!line.StartsWith("category=", StringComparison.OrdinalIgnoreCase)) continue;
            string[] bits = line.Substring(9).Split('|');
            if (bits.Length < 3) continue;
            Categories.Add(new Category {
                Name = bits[0].Trim(), Exe = bits[1].Trim(), Args = bits[2].Trim(),
                Shows = bits.Length > 3 ? bits[3].Trim() : Path.GetFileNameWithoutExtension(bits[1].Trim()),
                InDock = bits.Length > 4 && bits[4].Trim() == "1",
                Icon = bits.Length > 5 ? bits[5].Trim() : "",
                HotKey = bits.Length > 6 ? bits[6].Trim() : "",
                // a file written before Reset existed: the shortcut it has counts as the suggested one
                DefaultKey = bits.Length > 7 ? bits[7].Trim() : (bits.Length > 6 ? bits[6].Trim() : ""),
                InCycle = !(bits.Length > 8 && bits[8].Trim().ToLowerInvariant() == "no"),
                KeyOn = !(bits.Length > 9 && bits[9].Trim().ToLowerInvariant() == "off")
            });
        }
        // A file from before shortcuts existed: fill in the suggested ones. Once the file has a
        // next= line this never happens again, so a shortcut you cleared stays cleared.
        if (!keysSeen) SuggestShortcuts();
        if (!nextDefaultSeen || !prevDefaultSeen)
        {
            // written before Reset existed: work the suggestion out again rather than assume the
            // keys there now are it - they may already have been changed or cleared
            string next, prev;
            SuggestPair(out next, out prev);
            if (!nextDefaultSeen) NextDefault = next;
            if (!prevDefaultSeen) PrevDefault = prev;
            Suggested = true;
        }
        return true;
    }

    // Set when Load() had to fill in suggested shortcuts. The dock saves them once, so they stay
    // what they were rather than being worked out again every time.
    public static bool Suggested;

    // Ctrl+Alt+1, 2, 3... for the categories in order, and Ctrl+Alt+Space / Ctrl+Alt+Shift+Space
    // for next and previous - each only if it is usable here, otherwise the nearest that is (see
    // SuggestKey; PageDown/PageUp, then Right/Left, for the pair). On this PC every Ctrl+Alt+number,
    // with and without Shift, and Ctrl+Alt+Space were already held by other programs.
    static void SuggestShortcuts()
    {
        for (int i = 0; i < Categories.Count; i++)
            if (Categories[i].HotKey.Length == 0)
                Categories[i].HotKey = Categories[i].DefaultKey = SuggestKey(Categories[i].Name, i + 1);
        SuggestPair(out NextKey, out PrevKey);
        Suggested = true;
    }

    // next and previous go together: the first pair of which both are usable here
    static void SuggestPair(out string next, out string prev)
    {
        next = prev = "";
        string[][] pairs = {
            new[] { "Ctrl+Alt+Space", "Ctrl+Alt+Shift+Space" },
            new[] { "Ctrl+Alt+PageDown", "Ctrl+Alt+PageUp" },
            new[] { "Ctrl+Alt+Right", "Ctrl+Alt+Left" } };
        foreach (var p in pairs)
            if (Usable(p[0]) && Usable(p[1])) { next = p[0]; prev = p[1]; return; }
    }

    // Usable: types no character on this keyboard (on a Polish one Ctrl+Alt+letter is AltGr:
    // ą, ę, ś...), no category here has it already, and no other program holds it.
    static bool Usable(string s)
    {
        return Shortcut.TypesCharacter(s) == null &&
               !Categories.Any(c => string.Equals(c.HotKey, s, StringComparison.OrdinalIgnoreCase)) &&
               Shortcut.Free(s);
    }

    // The shortcut suggested for the n-th category: Ctrl+Alt+n, then Ctrl+Alt+Shift+n, then
    // Ctrl+Alt+ a letter of its name - Work gets W, unless W types something or is taken - then
    // Ctrl+Alt+F-n. The first one usable here, or none.
    public static string SuggestKey(string name, int n)
    {
        var tries = new List<string>();
        if (n <= 9) { tries.Add("Ctrl+Alt+" + n); tries.Add("Ctrl+Alt+Shift+" + n); }
        foreach (char ch in name.ToUpperInvariant()) if (ch >= 'A' && ch <= 'Z') tries.Add("Ctrl+Alt+" + ch);
        if (n <= 12) tries.Add("Ctrl+Alt+F" + n);
        foreach (string s in tries) if (Usable(s)) return s;
        return "";
    }

    public static void Save()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Browser Switch. Edit by hand if you like - the window writes the same thing.");
        sb.AppendLine("# category=<name>|<browser exe>|<profile arguments>|<what to show>|<in dock: 1>|<dock icon>|<shortcut>");
        sb.AppendLine();
        sb.AppendLine("active=" + Active);
        if (FallbackExe.Length > 0) sb.AppendLine("fallback=" + FallbackExe);
        sb.AppendLine("shortcuts=" + (ShortcutsOn ? "on" : "off"));
        sb.AppendLine("docktips=" + (TipSwitchTo ? "switch" : "name"));
        sb.AppendLine("next=" + NextKey);
        sb.AppendLine("previous=" + PrevKey);
        sb.AppendLine("default-next=" + NextDefault);
        sb.AppendLine("default-previous=" + PrevDefault);
        sb.AppendLine("next-on=" + (NextOn ? "yes" : "no"));
        sb.AppendLine("previous-on=" + (PrevOn ? "yes" : "no"));
        foreach (var c in Categories)
            sb.AppendLine("category=" + c.Name + "|" + c.Exe + "|" + c.Args + "|" + c.Shows +
                          (c.InDock || c.Icon.Length > 0 || c.HotKey.Length > 0 || c.DefaultKey.Length > 0 || !c.InCycle || !c.KeyOn
                              ? "|" + (c.InDock ? "1" : "") + "|" + c.Icon + "|" + c.HotKey + "|" + c.DefaultKey +
                                "|" + (c.InCycle ? "" : "no") + "|" + (c.KeyOn ? "" : "off")
                              : ""));
        LastText = sb.ToString();
        // UTF-8 with a byte-order mark, so profile names with accents or dashes survive being read
        // back by anything else (PowerShell 5 assumes the old Windows code page without one).
        try { File.WriteAllText(File_, LastText, new UTF8Encoding(true)); } catch { }
    }

    // An empty "active" means no category is live on purpose - that is what the panic button does.
    // It must NOT quietly fall through to the first category, or the panic button would do nothing.
    public static Category Current()
    {
        if (Active.Length == 0) return null;
        return Categories.FirstOrDefault(c => string.Equals(c.Name, Active, StringComparison.OrdinalIgnoreCase));
    }
}

// ---- the program -------------------------------------------------------------------------------

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // A fault goes to errors.log, never into a dialog box - the dock runs unseen for days. This
        // has to come before anything makes a window: Windows refuses it afterwards, and 2.5.0 set
        // it too late, so the app failed to start at all.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => Note(e.Exception.ToString());
        Config.Load();

        try
        {
            // no link: the dock (notification area), opening the window - or, at sign-in, just the dock
            if (args.Length == 0) { Tray.Run(true); return 0; }
            if (args[0] == "--tray") { Tray.Run(false); return 0; }

            if (args[0] == "--switch" && args.Length > 1)
            {
                Config.Active = args[1]; Config.Save(); return 0;
            }
            // the panic button: no category is live, so links go to the browser that was the default
            // before any of this existed. Nothing is uninstalled and every category is still there.
            if (args[0] == "--reset")
            {
                Config.Active = ""; Config.Save(); return 0;
            }
            // Builds the whole window and reports what it holds, without ever showing it. Constructor
            // faults - a bad layout, a null list - are where a window like this breaks, and this
            // catches them without putting anything on screen.
            if (args[0] == "--selftest")
            {
                var sb = new StringBuilder();
                using (var f = new SwitchForm())
                {
                    sb.AppendLine("form built: " + f.Text + "  " + f.Width + "x" + f.Height);
                    sb.AppendLine("categories listed: " + f.CategoryCount);
                    sb.AppendLine("browsers in tree:  " + f.BrowserCount);
                    sb.AppendLine("profiles in tree:  " + f.ProfileCount);
                    sb.AppendLine("switch buttons:    " + f.SwitchButtonCount);
                    sb.AppendLine("header says:       " + f.HeaderText);
                    sb.AppendLine("is default browser: " + f.IsDefaultBrowser + (f.IsDefaultBrowser ? "" : "   (warning strip shown)"));
                }
                // every dock icon is built too - the way a picked file or colour would fail
                var docked = Config.Categories.Where(c => c.InDock).Select(c => c.Name).ToList();
                sb.AppendLine("in the dock:       " + (docked.Count > 0 ? string.Join(", ", docked) : "(none)"));
                sb.AppendLine("hover texts:       " + string.Join(" / ", Config.Categories.Where(c => c.InDock).Select(c => "\"" + Tray.TipFor(c) + "\"")));
                int built = 0;
                foreach (var c in Config.Categories) using (var icon = Tray.IconFor(c)) if (icon != null) built++;
                sb.AppendLine("dock icons built:  " + built + " of " + Config.Categories.Count);
                // shortcuts: what is set, whether any would type a character here, whether each is free
                var keys = Config.Categories.Select(c => c.HotKey).Concat(new[] { Config.NextKey, Config.PrevKey })
                                 .Where(k => k.Length > 0).ToList();
                sb.AppendLine("shortcuts:         " + (Config.ShortcutsOn ? "on" : "off") + " - " +
                    string.Join(", ", Config.Categories.Select(c => c.Name + " " + (c.HotKey.Length > 0 ? c.HotKey : "(none)") + (c.KeyOn ? "" : " (off)"))) +
                    ", next " + (Config.NextKey.Length > 0 ? Config.NextKey : "(none)") + (Config.NextOn ? "" : " (off)") +
                    ", previous " + (Config.PrevKey.Length > 0 ? Config.PrevKey : "(none)") + (Config.PrevOn ? "" : " (off)"));
                sb.AppendLine("next/previous go:  " + string.Join(", ", Config.Categories.Where(c => c.InCycle).Select(c => c.Name)) +
                    (Config.Categories.Any(c => !c.InCycle) ? "   (skipping " + string.Join(", ", Config.Categories.Where(c => !c.InCycle).Select(c => c.Name)) + ")" : ""));
                var changed = Config.Categories.Where(c => c.HotKey != c.DefaultKey).Select(c => c.Name + " " + c.DefaultKey + " -> " + c.HotKey).ToList();
                if (Config.NextKey != Config.NextDefault) changed.Add("next " + Config.NextDefault + " -> " + Config.NextKey);
                if (Config.PrevKey != Config.PrevDefault) changed.Add("previous " + Config.PrevDefault + " -> " + Config.PrevKey);
                sb.AppendLine("changed from suggested: " + (changed.Count > 0 ? string.Join(", ", changed) : "none"));
                var typing = keys.Where(k => Shortcut.TypesCharacter(k) != null).Select(k => k + " types " + Shortcut.TypesCharacter(k)).ToList();
                sb.AppendLine("types a character: " + (typing.Count > 0 ? string.Join(", ", typing) : "none"));
                var unreadable = keys.Where(k => { uint m; Keys key; return !Shortcut.TryParse(k, out m, out key) ||
                                     Shortcut.Format((m & Shortcut.Ctrl) != 0, (m & Shortcut.Alt) != 0, (m & Shortcut.Shift) != 0, (m & Shortcut.Win) != 0, key) != k; }).ToList();
                sb.AppendLine("read back the same:" + (unreadable.Count == 0 ? " all" : " NOT " + string.Join(", ", unreadable)));
                using (var probe = new HotkeyWindow())
                {
                    var busy = keys.Where(k => !probe.IsFree(k)).ToList();
                    sb.AppendLine("free right now:    " + (busy.Count == 0 ? "all" : "not " + string.Join(", ", busy) + " (held by the dock itself, or another program)"));
                }
                File.WriteAllText(Path.Combine(Config.Dir, "selftest.txt"), sb.ToString());
                return 0;
            }
            if (args[0] == "--list")     // what it can see, written to a file - for checking without the window
            {
                var sb = new StringBuilder();
                foreach (var b in Machine.Browsers())
                {
                    sb.AppendLine(b.Name + "  [" + b.Exe + "]");
                    foreach (var p in b.Profiles) sb.AppendLine("    " + p.Name + "   ->   " + p.Args);
                }
                File.WriteAllText(Path.Combine(Config.Dir, "detected.txt"), sb.ToString());
                return 0;
            }
            if (args[0] == "--dry")
            {
                string exe, extra, why;
                Resolve(out exe, out extra, out why);
                File.WriteAllText(Path.Combine(Config.Dir, "dry-run.log"),
                    why + "\t" + exe + "\t" + extra + "\t" + (args.Length > 1 ? args[1] : ""));
                return 0;
            }
            Open(args[0]);
            return 0;
        }
        catch (Exception ex)
        {
            Note(ex.ToString());
            string url = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : null;
            // A clicked link must still arrive somewhere, so it goes to whatever browser exists.
            // Without a link there is nothing to deliver - opening a browser anyway just looks like
            // the app "opens Firefox". If you clicked the app itself, it says what happened instead.
            if (url != null)
            {
                try
                {
                    if (Config.FallbackExe.Length > 0 && File.Exists(Config.FallbackExe)) Launch(Config.FallbackExe, "", url);
                    else { var b = Machine.Browsers().FirstOrDefault(); if (b != null) Launch(b.Exe, "", url); }
                }
                catch { }
            }
            else if (args.Length == 0)
                MessageBox.Show("Browser Switch could not start.\r\n\r\nWhat went wrong is written in errors.log, in\r\n" + Config.Dir,
                                "Browser Switch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }

    // Where a link goes, and why. One place, so that --dry can never disagree with what really
    // happens: the live category if it has a working browser, otherwise the browser that was the
    // default before any of this existed (recorded at install time, so an unconfigured machine
    // behaves exactly as it did before), otherwise whatever browser Windows lists first - because
    // any browser beats a link that does nothing.
    public static void Resolve(out string exe, out string extra, out string why)
    {
        var c = Config.Current();
        if (c != null && c.Exe.Length > 0 && File.Exists(c.Exe))
        { exe = c.Exe; extra = c.Args; why = Config.Active; return; }

        extra = "";
        string reason = c != null ? c.Name + " has no browser"
                      : Config.Categories.Count > 0 ? "no category live"
                      : "nothing set up";
        if (Config.FallbackExe.Length > 0 && File.Exists(Config.FallbackExe))
        { exe = Config.FallbackExe; why = "fallback (" + reason + ")"; return; }

        var b = Machine.Browsers().FirstOrDefault();
        exe = b == null ? "" : b.Exe;
        why = "fallback (first browser found)";
    }

    public static void Open(string url)
    {
        string exe, extra, why;
        Resolve(out exe, out extra, out why);
        if (exe.Length > 0) Launch(exe, extra, url);
    }

    public static void Launch(string exe, string profileArgs, string url)
    {
        string arguments = profileArgs ?? "";
        if (!string.IsNullOrEmpty(url)) arguments = (arguments + " \"" + url.Replace("\"", "") + "\"").Trim();
        var psi = new ProcessStartInfo(exe, arguments) { UseShellExecute = false };
        Process.Start(psi);
    }

    public static void Note(string text)
    {
        try { File.AppendAllText(Path.Combine(Config.Dir, "errors.log"), DateTime.Now + "\r\n" + text + "\r\n\r\n"); } catch { }
    }
}
