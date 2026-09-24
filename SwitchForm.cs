// The window you get when you click Browser Switch.
//
// Top    - what links open in right now, and the switch for all rules.
// Left   - your categories, and what each one points at.
// Right  - every browser on this machine, with its profiles underneath.
// Bottom - one button per category (click it and links go there from that moment on), Rules…
//          and Shortcuts…. Explanations sit behind the small "?" marks (Ui.cs).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

class SwitchForm : Form
{
    readonly List<Browser> browsers = Machine.Browsers();
    readonly ToolTip hints = new ToolTip();
    Label header, caption;
    PictureBox headerIcon;
    CheckBox useRules;
    Label rulesCount;
    ListBox categoryList;
    TreeView browserTree;
    Button assign, rename, remove, iconButton, rulesButton;
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
        ShowInTaskbar = false;     // it lives in the dock; closing it hides it back there
        Size = new Size(760, 580);
        MinimumSize = new Size(640, 460);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        // Minimum sizes and the splitter position are set further down, once this is inside the form.
        // A SplitContainer is 150px wide until it is docked, and setting them here throws.
        var split = new SplitContainer { Dock = DockStyle.Fill };
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
        browserTree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowLines = true, ItemHeight = 22 };
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
        var extras = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, Padding = new Padding(4, 10, 10, 8), WrapContents = false };
        rulesButton = Button_("Rules…", delegate { EditRules(); });
        rulesButton.Height = 32;
        hints.SetToolTip(rulesButton, "Send links from chosen apps or websites to a chosen category");
        extras.Controls.Add(rulesButton);
        var keysButton = Button_("Shortcuts…", delegate { EditShortcuts(); });
        keysButton.Height = 32;
        hints.SetToolTip(keysButton, "Keyboard shortcuts for switching");
        extras.Controls.Add(keysButton);
        bottom.Controls.Add(switchRow);
        bottom.Controls.Add(extras);

        Controls.Add(split);
        Controls.Add(Header());
        Controls.Add(NotDefaultWarning());
        Controls.Add(bottom);

        // now that it has a real width, the panels can be given their limits
        if (split.Width > 480)
        {
            split.Panel1MinSize = 220;
            split.Panel2MinSize = 240;
            split.SplitterDistance = Math.Min(340, split.Width - 260);
        }

        FillBrowsers();
        Reload();
    }

    // The top: where links go right now, and the switch for all rules.
    Control Header()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = SystemColors.Window };
        headerIcon = new PictureBox { Left = 16, Top = 14, Size = new Size(32, 32), SizeMode = PictureBoxSizeMode.Zoom };
        caption = new Label { Left = 58, Top = 9, AutoSize = true, ForeColor = SystemColors.GrayText };
        header = new Label { Left = 58, Top = 26, AutoSize = true, Font = new Font("Segoe UI", 12F, FontStyle.Bold) };

        useRules = new CheckBox { Text = "Use rules", AutoSize = true, Margin = new Padding(3, 5, 3, 3) };
        useRules.CheckedChanged += delegate { if (filling) return; Config.RulesOn = useRules.Checked; Save(); Reload(); };
        rulesCount = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 0) };
        var row = new FlowLayoutPanel { Left = 12, Top = 58, AutoSize = true, WrapContents = false };
        row.Controls.Add(useRules);
        row.Controls.Add(rulesCount);
        row.Controls.Add(new HelpMark(
            "One switch for all your link rules. Off: every link simply opens in the live category - switch " +
            "category and links follow. On: a link that matches a rule goes where the rule says.\n" +
            "Also in the dock's right-click menu, and on a keyboard shortcut (see Shortcuts…). " +
            "Rules… sets up the rules themselves.")
            { Margin = new Padding(6, 6, 0, 0) });

        panel.Controls.Add(headerIcon);
        panel.Controls.Add(caption);
        panel.Controls.Add(header);
        panel.Controls.Add(row);
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

    Control NotDefaultWarning()
    {
        var strip = new Panel { Dock = DockStyle.Top, Height = 0, BackColor = Color.FromArgb(255, 244, 206) };
        if (IsDefaultBrowser) return strip;

        strip.Height = 46;
        strip.Padding = new Padding(10, 8, 12, 8);
        var say = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        say.Controls.Add(new Label { AutoSize = true, ForeColor = Color.FromArgb(90, 60, 0), Font = new Font(Font, FontStyle.Bold),
                                     Margin = new Padding(3, 6, 0, 0),
                                     Text = "⚠  Not switched on yet - links still go to your old browser" });
        say.Controls.Add(new HelpMark(
            "Windows sends every link to your default browser, and only you can change that - no program is " +
            "allowed to. Until Browser Switch is the default for HTTP and HTTPS, nothing in this window has any " +
            "effect.\nThe button opens the right page in Settings.") { Margin = new Padding(6, 7, 0, 0) });
        var open = new Button { Dock = DockStyle.Right, Width = 170, Text = "Fix this in Settings", AutoSize = false };
        open.Click += delegate {
            // Windows 11 can open straight at one app's page, which saves hunting through the list.
            // The name here has to be the value name under RegisteredApplications - "BrowserSwitch",
            // no space. The display name "Browser Switch" matches nothing and lands on a blank page.
            // If that form of the link is not understood, the plain Default apps page still opens.
            try { System.Diagnostics.Process.Start("ms-settings:defaultapps?registeredAppUser=BrowserSwitch"); }
            catch { try { System.Diagnostics.Process.Start("ms-settings:defaultapps"); } catch { } }
        };
        strip.Controls.Add(say);
        strip.Controls.Add(open);
        return strip;
    }

    // read by --selftest, so the window can be checked without being shown
    public int CategoryCount { get { return categoryList.Items.Count; } }
    public int BrowserCount { get { return browserTree.Nodes.Count; } }
    public int ProfileCount { get { int n = 0; foreach (TreeNode t in browserTree.Nodes) n += t.Nodes.Count; return n; } }
    public int SwitchButtonCount { get { return switchRow.Controls.OfType<Button>().Count(); } }
    public string HeaderText { get { return (caption.Text + " " + header.Text).Trim(); } }

    static Button Button_(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, Height = 26, AutoSize = true, Padding = new Padding(6, 0, 6, 0) };
        b.Click += onClick;
        return b;
    }

    // ---- filling in -----------------------------------------------------------------------------

    void FillBrowsers()
    {
        browserTree.BeginUpdate();
        browserTree.Nodes.Clear();
        foreach (var b in browsers)
        {
            var node = new TreeNode(b.Name) { Tag = b };
            foreach (var p in b.Profiles) node.Nodes.Add(new TreeNode(p.Name) { Tag = new object[] { b, p } });
            browserTree.Nodes.Add(node);
            node.Expand();
        }
        browserTree.EndUpdate();
        if (browsers.Count == 0)
            browserTree.Nodes.Add(new TreeNode("No browsers found - is anything installed?"));
    }

    public void Reload()
    {
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
            switchRow.Controls.Add(new Label { Text = "Switch to:", AutoSize = true, Margin = new Padding(3, 9, 0, 0) });
            switchRow.Controls.Add(new HelpMark("Click a category to make it live: from then on links open in its browser. " +
                "While rules are on, a link that matches a rule still goes where the rule says.")
                { Margin = new Padding(5, 10, 8, 0) });
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
        rulesButton.Text = Config.RulesOn && rules > 0 ? "Rules (" + rules + ")…" : "Rules…";
        UpdateButtons();
    }

    // The top line says where a link goes now: the live category (with a word about rules, if any
    // are in use), or the original browser.
    void ShowHeader()
    {
        filling = true;
        int ticked = Config.Rules.Count(r => r.On);
        useRules.Checked = Config.RulesOn;
        rulesCount.Text = ticked == 0 ? "(no rules ticked yet)" : ticked == 1 ? "(1 rule)" : "(" + ticked + " rules)";
        filling = false;

        var live = Config.Current();
        var shown = live;
        bool rulesInUse = Config.RulesOn && ticked > 0;
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

    void EditRules()
    {
        using (var d = new RulesDialog(Save)) d.ShowDialog(this);
        Reload();
    }

    void EditShortcuts()
    {
        if (PauseShortcuts != null) PauseShortcuts(true);
        try { using (var d = new ShortcutsDialog(ShortcutFree, Save)) d.ShowDialog(this); }
        finally { if (PauseShortcuts != null) PauseShortcuts(false); }
        Reload();
    }

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
