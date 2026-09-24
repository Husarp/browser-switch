// The window you get when you click Browser Switch.
//
// Top    - what links open in right now.
// Tabs   - Categories: your categories on the left, every browser and its profiles on the right.
//          Rules (Rules.cs), Shortcuts (Shortcuts.cs), and the rest.
// Bottom - one button per category: click it and links go there from that moment on.
// Explanations sit behind the small "?" marks (Ui.cs). Until Browser Switch is the default browser,
// the setup screen (Setup.cs) shows instead.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

partial class SwitchForm : Form
{
    readonly List<Browser> browsers = Machine.Browsers();
    Label header, caption;
    PictureBox headerIcon;
    ListBox categoryList;
    BrowserTree browserTree;
    Button assign, rename, remove, iconButton;
    TabControl tabs;
    readonly ImageList treeIcons = new ImageList { ImageSize = new Size(20, 20), ColorDepth = ColorDepth.Depth32Bit };
    TabPage categoriesPage, rulesPage, shortcutsPage, cleaningPage, logPage, aboutPage;
    SplitContainer split;
    bool keysPaused;           // the dock's shortcuts are let go while the Shortcuts tab is open
    Control warning;           // the yellow "not switched on yet" strip
    CheckBox inDock;
    PictureBox iconPreview;
    bool filling;              // true while controls are being set to match the settings, not by you
    FlowLayoutPanel switchRow;

    // The dock listens for this, so a change made here shows up there at once.
    public Action Saved;
    // Set by the dock: stop its shortcuts while new ones are recorded, and ask whether a
    // combination is free. Without a dock both stay null and the Shortcuts window still works.
    public Action<bool> PauseShortcuts;
    public Func<string, bool> ShortcutFree;

    public SwitchForm()
    {
        Text = "Browser Switch";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        ShowInTaskbar = Config.TaskbarButton;   // a taskbar button while open, unless set to live in the dock only
        Activated += delegate { CheckDefault(); };   // Settings may have changed the default browser meanwhile
        Size = new Size(860, 640);
        MinimumSize = new Size(700, 500);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        // Minimum sizes and the splitter position are set further down, once this is inside the form.
        // A SplitContainer is 150px wide until it is docked, and setting them here throws.
        split = new SplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Padding = new Padding(10, 6, 6, 8);
        split.Panel2.Padding = new Padding(6, 6, 10, 8);

        // ---- left: the categories ----
        categoryList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 42 };
        categoryList.DrawItem += DrawRow;
        categoryList.SelectedIndexChanged += delegate { UpdateButtons(); };
        categoryList.DoubleClick += delegate { SwitchTo(Selected()); };

        var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(0, 4, 0, 0) };
        var add = Button_("New category", delegate { NewCategory(); });
        rename = Button_("Rename", delegate { RenameCategory(); });
        remove = Button_("Delete", delegate { DeleteCategory(); });
        leftButtons.Controls.AddRange(new Control[] { add, rename, remove });

        // pin the selected category to the dock, and choose the icon it shows there
        inDock = new CheckBox { Text = "Show in dock", AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
        inDock.CheckedChanged += delegate { if (!filling) SetInDock(inDock.Checked); };
        iconButton = Button_("Dock icon…", delegate { PickIcon(); });
        iconPreview = new PictureBox { Size = new Size(24, 24), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(6, 5, 0, 0) };
        var dockHelp = new HelpMark("Show in dock puts this category's own icon next to the clock. One click on that icon " +
                                    "switches to it - no window needed.\nDock icon… chooses what the icon looks like: the " +
                                    "browser's own icon, that icon recoloured, a colour with a letter, or an image file.")
                       { Margin = new Padding(8, 8, 0, 0) };
        var dockRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(0, 2, 0, 0), WrapContents = false };
        dockRow.Controls.AddRange(new Control[] { inDock, iconButton, iconPreview, dockHelp });

        split.Panel1.Controls.Add(categoryList);
        split.Panel1.Controls.Add(Ui.Section("Categories",
            "A category is a name - Work, Home, School - with a browser and profile. Links open in the live one, " +
            "marked LIVE.\nTo set one up: select it here, pick a profile on the right, press Use this.\n" +
            "Double-click a category to make it live."));
        split.Panel1.Controls.Add(leftButtons);
        split.Panel1.Controls.Add(dockRow);

        // ---- right: browsers and their profiles ----
        browserTree = new BrowserTree { Dock = DockStyle.Fill, HideSelection = false, ShowLines = false, FullRowSelect = true,
                                     ItemHeight = 28, Indent = 26, ImageList = treeIcons, BorderStyle = BorderStyle.FixedSingle };
        browserTree.AfterSelect += delegate { UpdateButtons(); };
        browserTree.DoubleClick += delegate { Assign(); };

        assign = Button_("Use this for the selected category", delegate { Assign(); });
        var rightButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(0, 4, 0, 0) };
        rightButtons.Controls.Add(assign);

        split.Panel2.Controls.Add(browserTree);
        split.Panel2.Controls.Add(Ui.Section("Browsers and profiles",
            "Every browser on this computer, with its profiles underneath - read from the browsers themselves, " +
            "so the names are the ones you gave them.\nSelect a profile, then Use this - or double-click it."));
        split.Panel2.Controls.Add(rightButtons);

        // ---- bottom: one button per category, and the extra settings ----
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Ui.Bar };
        switchRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 4, 8), WrapContents = false, AutoScroll = true };
        bottom.Controls.Add(switchRow);

        // ---- the tabs: Categories holds the two panels above; the others are built when opened ----
        tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        categoriesPage = new TabPage("Categories") { UseVisualStyleBackColor = true };
        categoriesPage.Controls.Add(split);
        rulesPage = new TabPage("Rules") { UseVisualStyleBackColor = true };
        shortcutsPage = new TabPage("Shortcuts") { UseVisualStyleBackColor = true };
        cleaningPage = new TabPage("Link cleaning") { UseVisualStyleBackColor = true };
        logPage = new TabPage("Link log") { UseVisualStyleBackColor = true };
        aboutPage = new TabPage("About & updates") { UseVisualStyleBackColor = true };
        tabs.TabPages.AddRange(new[] { categoriesPage, rulesPage, shortcutsPage, cleaningPage, logPage, aboutPage });
        tabs.SelectedIndexChanged += delegate { ShowTab(); };
        FormClosed += delegate { PauseKeys(false); };

        var top = Header();
        warning = NotDefaultWarning();
        Controls.Add(tabs);
        Controls.Add(top);
        Controls.Add(warning);
        Controls.Add(bottom);
        mainScreen.AddRange(new[] { tabs, top, warning, bottom });

        // once the window is laid out and the panels have a real width, they can be given their limits
        Load += delegate
        {
            if (split.Width <= 480) return;
            split.Panel1MinSize = 220;
            split.Panel2MinSize = 240;
            split.SplitterDistance = Math.Min(360, split.Width - 280);
        };

        Ui.HandCursors(this);
        FillBrowsers();
        Reload();
        if (!IsDefaultBrowser || !Config.SetupDone) ShowSetup();
    }

    // The top: where links go right now.
    Control Header()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = SystemColors.Window };
        headerIcon = new PictureBox { Left = 16, Top = 14, Size = new Size(32, 32), SizeMode = PictureBoxSizeMode.Zoom };
        caption = new Label { Left = 58, Top = 9, AutoSize = true, ForeColor = SystemColors.GrayText };
        header = new Label { Left = 58, Top = 26, AutoSize = true, Font = new Font("Segoe UI", 12F, FontStyle.Bold) };

        panel.Controls.Add(headerIcon);
        panel.Controls.Add(caption);
        panel.Controls.Add(header);
        panel.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = SystemColors.ControlLight });
        return panel;
    }


    // Everything in this window is ignored by Windows until Browser Switch is the default browser,
    // and that has to be set by hand in Settings. Without saying so, the whole thing just looks
    // broken: you set up categories, click a link somewhere, and it opens in the old browser.
    public bool IsDefaultBrowser
    {
        get
        {
            // Windows 11 24H2 and later record the choice in UserChoiceLatest and leave the older
            // UserChoice as it was - on this PC it still said Firefox after Browser Switch had been
            // chosen in Settings. So the newer record wins whenever it exists.
            const string https = @"SOFTWARE\Microsoft\Windows\Shell\Associations\UrlAssociations\https\";
            try
            {
                string id = ProgIdAt(https + @"UserChoiceLatest\ProgId") ?? ProgIdAt(https + "UserChoice");
                return id != null && id.StartsWith("BrowserSwitch", StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }   // cannot tell: say nothing rather than nag wrongly
        }
    }

    static string ProgIdAt(string key)
    {
        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key))
            return k == null ? null : k.GetValue("ProgId") as string;
    }

    // The default browser now - its name, as Windows lists it, and its program - or null. Found
    // through the browsers' registrations: the one whose https handler is the chosen one.
    static string[] CurrentDefault()
    {
        const string https = @"SOFTWARE\Microsoft\Windows\Shell\Associations\UrlAssociations\https\";
        return BrowserFor(ProgIdAt(https + @"UserChoiceLatest\ProgId") ?? ProgIdAt(https + "UserChoice"));
    }

    // The browser whose https handler is this one ("FirefoxURL-308046B0AF4A39CB"): name and program.
    static string[] BrowserFor(string id)
    {
        if (id == null) return null;
        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
            using (var clients = hive.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet"))
            {
                if (clients == null) continue;
                foreach (string name in clients.GetSubKeyNames())
                    using (var urls = clients.OpenSubKey(name + @"\Capabilities\URLAssociations"))
                    using (var client = clients.OpenSubKey(name))
                    using (var open = clients.OpenSubKey(name + @"\shell\open\command"))
                    {
                        if (urls == null || !string.Equals(urls.GetValue("https") as string, id, StringComparison.OrdinalIgnoreCase)) continue;
                        string label = (client.GetValue("") as string) ?? name;
                        string command = open == null ? null : open.GetValue("") as string;
                        string exe = command == null ? null : command.Trim().StartsWith("\"") ? command.Trim().Substring(1).Split('"')[0] : command.Split(' ')[0];
                        return new[] { label, exe != null && File.Exists(exe) ? exe : null };
                    }
            }
        return null;
    }

    // Whether Browser Switch is the default browser can change while the window is open - in
    // Settings, or by another browser asking to be the default. Checked whenever the window comes
    // to the front: the yellow strip and the top line follow.
    void CheckDefault()
    {
        if (setup != null && setup.Visible) return;
        if (warning != null) warning.Visible = !IsDefaultBrowser;
        ShowHeader();
    }

    Control NotDefaultWarning()
    {
        var strip = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.FromArgb(255, 244, 206), Visible = !IsDefaultBrowser };
        strip.Padding = new Padding(10, 8, 12, 8);
        var say = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        say.Controls.Add(new Label { AutoSize = true, ForeColor = Color.FromArgb(90, 60, 0), Font = new Font(Font, FontStyle.Bold),
                                     Margin = new Padding(3, 6, 0, 0),
                                     Text = "⚠  Not switched on yet - links do not pass through Browser Switch" });
        say.Controls.Add(new HelpMark(
            "Windows sends every link to your default browser, and only you can change that - no program is " +
            "allowed to. Until Browser Switch is the default for HTTP and HTTPS, nothing in this window has any " +
            "effect.\nThe button opens the right page in Settings.") { Margin = new Padding(6, 7, 0, 0) });
        var open = new Button { Dock = DockStyle.Right, Width = 170, Text = "Set it up", AutoSize = false };
        open.Click += delegate { ShowSetup(); };
        strip.Controls.Add(say);
        strip.Controls.Add(open);
        return strip;
    }

    // read by --selftest, so the window can be checked without being shown
    public int CategoryCount { get { return categoryList.Items.Count; } }
    public int BrowserCount { get { return browserTree.Nodes.Count; } }
    public int ProfileCount { get { int n = 0; foreach (TreeNode t in browserTree.Nodes) n += t.Nodes.Count; return n; } }
    public int SwitchButtonCount { get { return switchRow.Controls.OfType<Button>().Count(); } }
    public string TabNames { get { return string.Join(", ", tabs.TabPages.Cast<TabPage>().Select(p => p.Text)); } }

    // Opens a tab by its name ("Rules", "Shortcuts"...) - for the dock menu and the tests.
    public void ShowTabNamed(string name)
    {
        foreach (TabPage p in tabs.TabPages)
            if (p.Text.StartsWith(name, StringComparison.OrdinalIgnoreCase)) { tabs.SelectedTab = p; ShowTab(); return; }
    }

    // A tab's contents are built fresh each time it is opened, so they always show the categories as
    // they are now - one added a moment ago on the Categories tab included.
    void ShowTab()
    {
        PauseKeys(tabs.SelectedTab == shortcutsPage);
        if (tabs.SelectedTab == rulesPage) Fill(rulesPage, new RulesPage(() => { Save(); Reload(); }));
        else if (tabs.SelectedTab == shortcutsPage) Fill(shortcutsPage, new ShortcutsPage(ShortcutFree, () => { Save(); Reload(); }));
        else if (tabs.SelectedTab == cleaningPage) Fill(cleaningPage, new CleaningPage(Save));
        else if (tabs.SelectedTab == logPage) Fill(logPage, new LogPage(Save));
        else if (tabs.SelectedTab == aboutPage) Fill(aboutPage, new AboutPage(Save, ShowSetup));
    }

    static void Fill(TabPage page, Control content)
    {
        foreach (var old in page.Controls.Cast<Control>().ToList()) old.Dispose();
        page.Controls.Add(content);
    }

    // While the Shortcuts tab is open the dock lets go of its keys, so pressing one records it here
    // instead of switching.
    void PauseKeys(bool pause)
    {
        if (pause == keysPaused) return;
        keysPaused = pause;
        if (PauseShortcuts != null) PauseShortcuts(pause);
    }
    public string HeaderText { get { return (caption.Text + " " + header.Text).Trim(); } }

    static Button Button_(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, Height = 26, AutoSize = true, Padding = new Padding(6, 0, 6, 0) };
        b.Click += onClick;
        return b;
    }

    // ---- filling in -----------------------------------------------------------------------------

    // Every browser with its own icon, in bold; under it its profiles, each with the profile's
    // picture where the browser keeps one (Brave, Chrome, Edge).
    void FillBrowsers()
    {
        browserTree.BeginUpdate();
        browserTree.Nodes.Clear();
        treeIcons.Images.Clear();
        using (var bold = new Font(Font, FontStyle.Bold))
            foreach (var b in browsers)
            {
                string own = TreeIcon(b.Exe, null);
                var node = new TreeNode(b.Name) { Tag = b, NodeFont = new Font(bold, FontStyle.Bold), ImageKey = own, SelectedImageKey = own };
                foreach (var p in b.Profiles)
                {
                    string pic = TreeIcon(b.Exe, p.Args) ?? own;
                    var child = new TreeNode(p.Name) { Tag = new object[] { b, p }, ImageKey = pic, SelectedImageKey = pic };
                    if (p.Args.Length == 0) child.ForeColor = SystemColors.GrayText;   // "(as it opens normally)"
                    node.Nodes.Add(child);
                }
                browserTree.Nodes.Add(node);
                node.Expand();
            }
        browserTree.EndUpdate();
        if (browsers.Count == 0)
            browserTree.Nodes.Add(new TreeNode("No browsers found - is anything installed?"));
        MarkUsedProfiles();
    }

    // The browser's icon, or the profile's own picture; its key in the list, or null if there is none.
    string TreeIcon(string exe, string args)
    {
        string file = args == null ? exe : Machine.ProfileIcon(exe, args);
        if (file == null || !File.Exists(file)) return null;
        if (treeIcons.Images.ContainsKey(file)) return file;
        try
        {
            using (var icon = file.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ? new Icon(file, 32, 32) : Icon.ExtractAssociatedIcon(file))
                treeIcons.Images.Add(file, icon.ToBitmap());
            return file;
        }
        catch { return null; }
    }

    // After a profile's name: which categories use it - "Work (Profile 2)   ←  Work".
    void MarkUsedProfiles()
    {
        foreach (TreeNode b in browserTree.Nodes)
            foreach (TreeNode n in b.Nodes)
            {
                var pair = n.Tag as object[]; if (pair == null) continue;
                var br = (Browser)pair[0]; var p = (Profile)pair[1];
                var users = Config.Categories.Where(c => string.Equals(c.Exe, br.Exe, StringComparison.OrdinalIgnoreCase) && c.Args == p.Args)
                                             .Select(c => c.Name).ToList();
                string text = p.Name + (users.Count > 0 ? "     ←  " + string.Join(", ", users) : "");
                if (n.Text != text) n.Text = text;
            }
    }

    public void Reload()
    {
        if (browserTree != null) MarkUsedProfiles();
        string keep = Selected() != null ? Selected().Name : null;

        categoryList.BeginUpdate();
        foreach (Row old in categoryList.Items) old.Dispose();
        categoryList.Items.Clear();
        foreach (var c in Config.Categories)
            categoryList.Items.Add(new Row(c, string.Equals(c.Name, Config.Active, StringComparison.OrdinalIgnoreCase)));
        categoryList.EndUpdate();

        for (int i = 0; i < categoryList.Items.Count; i++)
            if (((Row)categoryList.Items[i]).Cat.Name == keep) categoryList.SelectedIndex = i;
        if (categoryList.SelectedIndex < 0 && categoryList.Items.Count > 0) categoryList.SelectedIndex = 0;

        ShowHeader();

        foreach (var old in switchRow.Controls.Cast<Control>().ToList()) old.Dispose();
        if (Config.Categories.Count > 0)
        {
            // the "?" in front, so the label stays next to the buttons it names
            switchRow.Controls.Add(new HelpMark("Click a category to make it live: from then on links open in its browser. " +
                "While rules are on, a link that matches a rule still goes where the rule says.")
                { Margin = new Padding(3, 10, 6, 0) });
            switchRow.Controls.Add(new Label { Text = "Switch to:", AutoSize = true, Margin = new Padding(0, 9, 8, 0) });
        }
        foreach (var c in Config.Categories)
        {
            var cat = c;
            var b = new Button { Text = c.Name, Height = 32, AutoSize = true, Padding = new Padding(10, 0, 10, 0) };
            if (string.Equals(c.Name, Config.Active, StringComparison.OrdinalIgnoreCase))
            {
                b.Font = new Font(b.Font, FontStyle.Bold);
                b.Enabled = false;
            }
            b.Click += delegate { SwitchTo(cat); };
            switchRow.Controls.Add(b);
        }
        int rules = Config.Rules.Count(r => r.On);
        rulesPage.Text = Config.RulesOn && rules > 0 ? "Rules (" + rules + ")" : "Rules";
        UpdateButtons();
    }

    // The top line says where a link goes now: the live category (with a word about rules, if any
    // are in use), or the original browser.
    void ShowHeader()
    {
        int ticked = Config.Rules.Count(r => r.On);
        var live = Config.Current();
        var shown = live;
        bool rulesInUse = Config.RulesOn && ticked > 0;
        if (!IsDefaultBrowser)
        {
            // Windows sends links straight to the default browser - Browser Switch never sees them
            string[] other = CurrentDefault();
            caption.Text = "Browser Switch is not your default browser - links go straight to:";
            header.Text = other != null ? other[0] : "your default browser";
            var old = headerIcon.Image;
            headerIcon.Image = null;
            if (other != null && other[1] != null) try { using (var icon = Icon.ExtractAssociatedIcon(other[1])) headerIcon.Image = icon.ToBitmap(); } catch { }
            if (old != null) old.Dispose();
            return;
        }
        if (live != null)
        {
            caption.Text = rulesInUse ? "Links open in (unless a rule says otherwise):" : "Links open in:";
            header.Text = live.Name + "   →   " + live.Shows;
        }
        else
        {
            caption.Text = Config.Categories.Count == 0 ? "Nothing set up yet" : "No category is live - links open in:";
            header.Text = Config.Categories.Count == 0 ? "Make a category, then pick a browser for it"
                                                       : "Your original browser (" + Program.OriginalBrowserName() + ")";
        }
        var oldIcon = headerIcon.Image;
        if (shown != null) using (var icon = Tray.IconFor(shown)) headerIcon.Image = icon.ToBitmap();
        else headerIcon.Image = null;
        if (oldIcon != null) oldIcon.Dispose();
    }

    // one line in the category list: its icon, its name (and LIVE), and what it opens in
    class Row : IDisposable
    {
        public readonly Category Cat; public readonly bool Live; public Bitmap Picture;
        public Row(Category c, bool isLive)
        {
            Cat = c; Live = isLive;
            try { using (var icon = Tray.IconFor(c)) Picture = icon.ToBitmap(); } catch { }
        }
        public string Detail
        {
            get
            {
                return (Cat.Shows.Length > 0 ? Cat.Shows : "No browser yet - pick one on the right") +
                       (Config.ShortcutsOn && Cat.KeyOn && Cat.HotKey.Length > 0 ? "     ·     " + Cat.HotKey : "");
            }
        }
        public override string ToString() { return (Live ? "live: " : "") + Cat.Name + " - " + Detail; }   // for screen readers
        public void Dispose() { if (Picture != null) Picture.Dispose(); }
    }

    void DrawRow(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= categoryList.Items.Count) return;
        var row = (Row)categoryList.Items[e.Index];
        var g = e.Graphics;
        var r = e.Bounds;
        bool picked = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(picked ? Ui.Picked : categoryList.BackColor)) g.FillRectangle(bg, r);
        if (row.Picture != null) g.DrawImage(row.Picture, r.Left + 8, r.Top + (r.Height - 24) / 2, 24, 24);
        int x = r.Left + 42;
        using (var bold = new Font(Font, FontStyle.Bold))
        {
            TextRenderer.DrawText(g, row.Cat.Name, bold, new Point(x, r.Top + 4), SystemColors.WindowText, TextFormatFlags.NoPrefix);
            if (row.Live)
            {
                int w = TextRenderer.MeasureText(g, row.Cat.Name, bold, Size.Empty, TextFormatFlags.NoPrefix).Width;
                using (var small = new Font(Font.FontFamily, 7.5F, FontStyle.Bold))
                {
                    var tag = new Rectangle(x + w + 4, r.Top + 6, TextRenderer.MeasureText("LIVE", small).Width + 6, 15);
                    using (var fill = new SolidBrush(Ui.Accent)) g.FillRectangle(fill, tag);
                    TextRenderer.DrawText(g, "LIVE", small, tag, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }
        TextRenderer.DrawText(g, row.Detail, Font, new Point(x, r.Top + 22),
                              row.Cat.Exe.Length > 0 ? SystemColors.GrayText : Color.DarkOrange, TextFormatFlags.NoPrefix);
        if (e.Index < categoryList.Items.Count - 1)
            using (var line = new Pen(Color.FromArgb(235, 235, 235))) g.DrawLine(line, r.Left + 8, r.Bottom - 1, r.Right - 8, r.Bottom - 1);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }

    Category Selected()
    {
        var row = categoryList.SelectedItem as Row;
        return row == null ? null : row.Cat;
    }

    void UpdateButtons()
    {
        bool hasCat = Selected() != null;
        bool hasProfile = browserTree.SelectedNode != null && browserTree.SelectedNode.Tag is object[];
        rename.Enabled = remove.Enabled = hasCat;
        assign.Enabled = hasCat && hasProfile;
        assign.Text = hasCat ? "Use this for “" + Selected().Name + "”" : "Use this for the selected category";

        // the dock controls follow the selected category
        filling = true;
        var sel = Selected();
        inDock.Checked = sel != null && sel.InDock;
        inDock.Enabled = sel != null && sel.Exe.Length > 0;     // pinning needs a browser to switch to
        iconButton.Enabled = sel != null;
        var old = iconPreview.Image;
        if (sel != null) using (var icon = Tray.IconFor(sel)) iconPreview.Image = icon.ToBitmap();
        else iconPreview.Image = null;
        if (old != null) old.Dispose();
        filling = false;
    }

    // ---- actions ---------------------------------------------------------------------------------

    void Save() { Config.Save(); if (Saved != null) Saved(); }


    void SetInDock(bool on)
    {
        var c = Selected(); if (c == null) return;
        c.InDock = on;
        Save();
    }

    // What the selected category's dock icon shows.
    void PickIcon()
    {
        var c = Selected(); if (c == null) return;
        var pick = new ContextMenuStrip();
        pick.Items.Add("The browser's own icon (with the profile's picture, where it has one)", null,
                       delegate { c.Icon = ""; Save(); UpdateButtons(); });
        // two profiles of one browser: same shape, different colours, still recognisably that browser
        var recolour = pick.Items.Add("Recolour the browser's icon…", null, delegate
        {
            using (var ed = new IconEditor(c))
                if (ed.ShowDialog(this) == DialogResult.OK) { c.Icon = ed.Spec; Save(); UpdateButtons(); }
        });
        recolour.Enabled = c.Exe.Length > 0;
        pick.Items.Add("A colour with a letter…", null, delegate
        {
            using (var dlg = new ColorDialog { FullOpen = true })
                if (dlg.ShowDialog(this) == DialogResult.OK)
                { c.Icon = "color:" + ColorTranslator.ToHtml(Color.FromArgb(dlg.Color.R, dlg.Color.G, dlg.Color.B)); Save(); UpdateButtons(); }
        });
        pick.Items.Add("An image file…", null, delegate
        {
            using (var dlg = new OpenFileDialog { Title = "Dock icon for " + c.Name,
                       Filter = "Icons and pictures|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe|All files|*.*" })
                if (dlg.ShowDialog(this) == DialogResult.OK) { c.Icon = "file:" + dlg.FileName; Save(); UpdateButtons(); }
        });
        pick.Show(iconButton, new Point(0, iconButton.Height));
    }

    void NewCategory()
    {
        string name = Ask("Name for the new category", "Work");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (Config.Categories.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        { MessageBox.Show(this, "There is already a category called " + name + "."); return; }

        string key = Config.SuggestKey(name, Config.Categories.Count + 1);
        Config.Categories.Add(new Category { Name = name, Exe = "", Args = "", Shows = "", HotKey = key, DefaultKey = key });
        if (Config.Categories.Count == 1) Config.Active = name;
        Save();
        Reload();
        for (int i = 0; i < categoryList.Items.Count; i++)
            if (((Row)categoryList.Items[i]).Cat.Name == name) categoryList.SelectedIndex = i;
    }

    void RenameCategory()
    {
        var c = Selected(); if (c == null) return;
        string name = Ask("New name for “" + c.Name + "”", c.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (string.Equals(Config.Active, c.Name, StringComparison.OrdinalIgnoreCase)) Config.Active = name.Trim();
        foreach (var r in Config.Rules.Where(r => string.Equals(r.Category, c.Name, StringComparison.OrdinalIgnoreCase)))
            r.Category = name.Trim();      // rules follow the category to its new name
        c.Name = name.Trim();
        Save(); Reload();
    }

    void DeleteCategory()
    {
        var c = Selected(); if (c == null) return;
        if (MessageBox.Show(this, "Delete the category “" + c.Name + "”?", "Browser Switch",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Config.Categories.Remove(c);
        if (string.Equals(Config.Active, c.Name, StringComparison.OrdinalIgnoreCase))
            Config.Active = Config.Categories.Count > 0 ? Config.Categories[0].Name : "";
        Save(); Reload();
    }

    void Assign()
    {
        var c = Selected();
        var node = browserTree.SelectedNode;
        if (c == null || node == null || !(node.Tag is object[])) return;
        var pair = (object[])node.Tag;
        var b = (Browser)pair[0]; var p = (Profile)pair[1];

        c.Exe = b.Exe;
        c.Args = p.Args;
        c.Shows = p.Args.Length == 0 ? b.Name : b.Name + " — " + p.Name;
        Save();
        Reload();
    }

    void SwitchTo(Category c)
    {
        if (c == null) return;
        if (c.Exe.Length == 0)
        { MessageBox.Show(this, "“" + c.Name + "” has no browser yet. Pick one on the right first."); return; }
        Config.Active = c.Name;
        Save();
        Close();
    }

    // A one-line prompt. WinForms has no built-in one, and pulling in Visual Basic's just for this
    // would be silly.
    string Ask(string question, string preset)
    {
        using (var dlg = new Form { Text = "Browser Switch", Size = new Size(380, 165), FormBorderStyle = FormBorderStyle.FixedDialog,
                                    StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, Font = Font })
        {
            var label = new Label { Text = question, Left = 14, Top = 16, Width = 340, AutoSize = false, Height = 20 };
            var box = new TextBox { Text = preset, Left = 14, Top = 42, Width = 340 };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 190, Top = 80, Width = 78 };
            var no = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 276, Top = 80, Width = 78 };
            dlg.Controls.AddRange(new Control[] { label, box, ok, no });
            dlg.AcceptButton = ok; dlg.CancelButton = no;
            Ui.HandCursors(dlg);
            box.SelectAll();
            return dlg.ShowDialog(this) == DialogResult.OK ? box.Text : null;
        }
    }
}

// A small editor for a pinned category's dock icon: the browser's own icon, recoloured. Two profiles
// of the same browser - Brave for Work and Brave for Home - then look different in the dock, and
// both still look like Brave. Nothing changes until OK.
class IconEditor : Form
{
    readonly Category sample;   // a stand-in carrying the settings being tried
    readonly TrackBar hue, strength, bright;
    readonly PictureBox big = new PictureBox { Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom };
    readonly PictureBox actual = new PictureBox { Size = new Size(32, 32), SizeMode = PictureBoxSizeMode.CenterImage,
                                                  Margin = new Padding(14, 16, 3, 3) };

    public string Spec { get { return "tint:" + hue.Value + "," + strength.Value + "," + bright.Value; } }

    public IconEditor(Category c)
    {
        sample = new Category { Name = c.Name, Exe = c.Exe, Args = c.Args, Shows = c.Shows };
        int h = 0, s = 100, b = 0;
        if (c.Icon.StartsWith("tint:", StringComparison.OrdinalIgnoreCase))
            try { var v = c.Icon.Substring(5).Split(','); h = int.Parse(v[0]); s = int.Parse(v[1]); b = int.Parse(v[2]); } catch { }

        Text = "Dock icon for " + c.Name;
        Font = new Font("Segoe UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        var previews = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(3, 0, 3, 8) };
        previews.Controls.Add(big);
        previews.Controls.Add(actual);
        previews.Controls.Add(new Label { AutoSize = true, Margin = new Padding(12, 24, 3, 3), ForeColor = SystemColors.GrayText,
                                          Text = "large, and at its real size" });
        table.Controls.Add(previews, 0, 0);
        table.SetColumnSpan(previews, 2);

        hue = Slider(table, 1, "Colour", -180, 180, h, 30);
        strength = Slider(table, 2, "Strength", 0, 200, s, 25);
        bright = Slider(table, 3, "Brightness", -50, 50, b, 10);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill,
                                            Margin = new Padding(0, 8, 0, 0) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var reset = new Button { Text = "Reset", AutoSize = true };
        reset.Click += delegate { hue.Value = 0; strength.Value = 100; bright.Value = 0; };
        buttons.Controls.AddRange(new Control[] { cancel, ok, reset });
        table.Controls.Add(buttons, 0, 4);
        table.SetColumnSpan(buttons, 2);
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(table);
        ShowPreview();
        Ui.HandCursors(this);
    }

    TrackBar Slider(TableLayoutPanel table, int row, string name, int min, int max, int value, int tick)
    {
        var label = new Label { Text = name, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 10, 12, 3) };
        var bar = new TrackBar { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)),
                                 TickFrequency = tick, SmallChange = 1, LargeChange = tick, Width = 280 };
        bar.ValueChanged += delegate { ShowPreview(); };
        table.Controls.Add(label, 0, row);
        table.Controls.Add(bar, 1, row);
        return bar;
    }

    void ShowPreview()
    {
        sample.Icon = Spec;
        using (var icon = Tray.IconFor(sample))
        {
            var oldBig = big.Image;
            var oldActual = actual.Image;
            big.Image = icon.ToBitmap();
            actual.Image = icon.ToBitmap();
            if (oldBig != null) oldBig.Dispose();
            if (oldActual != null) oldActual.Dispose();
        }
    }
}

// The browsers and profiles list. Windows draws the small open / close arrow at a fixed size, which
// is hard to hit, so the whole row of a browser does the same: one click anywhere on it - its name,
// its icon, the space after - opens or closes its profiles. The arrows are drawn in the modern style
// File Explorer uses.
class BrowserTree : TreeView
{
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr window, string app, string idList);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { SetWindowTheme(Handle, "explorer", null); } catch { }
    }

    protected override void OnNodeMouseClick(TreeNodeMouseClickEventArgs e)
    {
        base.OnNodeMouseClick(e);
        // a click on the arrow itself is handled by Windows already
        if (e.Button == MouseButtons.Left && e.Node.Level == 0 && HitTest(e.Location).Location != TreeViewHitTestLocations.PlusMinus)
            e.Node.Toggle();
    }

    // Windows also opens or closes a row on a double-click; on a browser's row that would undo the
    // click just made, so a second quick click there counts as one more click. On a profile a
    // double-click stays what it was: use this profile.
    protected override void WndProc(ref Message m)
    {
        const int WM_LBUTTONDBLCLK = 0x0203;
        if (m.Msg == WM_LBUTTONDBLCLK)
        {
            var at = new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF));
            var hit = HitTest(at);
            if (hit.Node != null && hit.Node.Level == 0) { hit.Node.Toggle(); return; }
        }
        base.WndProc(ref m);
    }

    // the hand over a browser's row, since the whole row can be clicked
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var node = GetNodeAt(e.Location);
        Cursor = node != null && node.Level == 0 ? Cursors.Hand : Cursors.Default;
    }
}
