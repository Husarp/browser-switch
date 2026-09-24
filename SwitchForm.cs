// The window you get when you click Browser Switch.
//
// Top    - what links open in right now.
// Left   - your categories, and what each one points at.
// Right  - every browser on this machine, with its profiles underneath.
// Bottom - one button per category. Click it and links go there from that moment on.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

class SwitchForm : Form
{
    readonly List<Browser> browsers = Machine.Browsers();
    Label header;
    ListBox categoryList;
    TreeView browserTree;
    Button assign, rename, remove, iconButton;
    CheckBox inDock;
    PictureBox iconPreview;
    bool filling;              // true while the dock controls are being set to match the selection
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
        Size = new Size(720, 520);
        MinimumSize = new Size(600, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        header = new Label { Dock = DockStyle.Top, Height = 46, Padding = new Padding(14, 12, 14, 0),
                             Font = new Font("Segoe UI", 11F, FontStyle.Bold) };

        // Minimum sizes and the splitter position are set further down, once this is inside the form.
        // A SplitContainer is 150px wide until it is docked, and setting them here throws.
        var split = new SplitContainer { Dock = DockStyle.Fill };

        // ---- left: the categories ----
        categoryList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        categoryList.SelectedIndexChanged += delegate { UpdateButtons(); };
        categoryList.DoubleClick += delegate { SwitchTo(Selected()); };

        var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(4) };
        var add = Button_("New category", delegate { NewCategory(); });
        rename = Button_("Rename", delegate { RenameCategory(); });
        remove = Button_("Delete", delegate { DeleteCategory(); });
        leftButtons.Controls.AddRange(new Control[] { add, rename, remove });

        // pin the selected category to the dock, and choose the icon it shows there
        inDock = new CheckBox { Text = "Show in dock", AutoSize = true, Padding = new Padding(2, 5, 0, 0) };
        inDock.CheckedChanged += delegate { if (!filling) SetInDock(inDock.Checked); };
        iconButton = Button_("Dock icon…", delegate { PickIcon(); });
        iconPreview = new PictureBox { Size = new Size(24, 24), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(6, 5, 0, 0) };
        var dockRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(4), WrapContents = false };
        dockRow.Controls.AddRange(new Control[] { inDock, iconButton, iconPreview });

        var leftHead = new Label { Dock = DockStyle.Top, Height = 24, Text = "  Categories", Padding = new Padding(4, 5, 0, 0),
                                   Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
        split.Panel1.Controls.Add(categoryList);
        split.Panel1.Controls.Add(leftHead);
        split.Panel1.Controls.Add(leftButtons);
        split.Panel1.Controls.Add(dockRow);

        // ---- right: browsers and their profiles ----
        browserTree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowLines = true };
        browserTree.AfterSelect += delegate { UpdateButtons(); };
        browserTree.DoubleClick += delegate { Assign(); };

        assign = Button_("Use this for the selected category", delegate { Assign(); });
        assign.AutoSize = true;
        var rightButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(4) };
        rightButtons.Controls.Add(assign);

        var rightHead = new Label { Dock = DockStyle.Top, Height = 24, Text = "  Browsers on this computer",
                                    Padding = new Padding(4, 5, 0, 0), Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
        split.Panel2.Controls.Add(browserTree);
        split.Panel2.Controls.Add(rightHead);
        split.Panel2.Controls.Add(rightButtons);

        // ---- bottom: one button per category ----
        switchRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(10, 9, 10, 9),
                                          BackColor = SystemColors.ControlLight, WrapContents = false, AutoScroll = true };

        Controls.Add(split);
        Controls.Add(header);
        Controls.Add(NotDefaultWarning());
        Controls.Add(switchRow);

        // now that it has a real width, the panels can be given their limits
        if (split.Width > 480)
        {
            split.Panel1MinSize = 200;
            split.Panel2MinSize = 240;
            split.SplitterDistance = Math.Min(300, split.Width - 260);
        }

        FillBrowsers();
        Reload();
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

        strip.Height = 76;
        strip.Padding = new Padding(14, 8, 14, 8);
        var text = new Label {
            Dock = DockStyle.Fill, ForeColor = Color.FromArgb(90, 60, 0), AutoSize = false,
            Text = "NOT SWITCHED ON. Windows is still sending every link to your old browser, so nothing\r\n" +
                   "below has any effect. Set “Browser Switch” as the default for HTTP and HTTPS.\r\n" +
                   "Windows only accepts that from you — no program is allowed to do it.",
        };
        var open = new Button { Dock = DockStyle.Right, Width = 170, Text = "Fix this in Settings", AutoSize = false };
        open.Click += delegate {
            // Windows 11 can open straight at one app's page, which saves hunting through the list.
            // The name here has to be the value name under RegisteredApplications - "BrowserSwitch",
            // no space. The display name "Browser Switch" matches nothing and lands on a blank page.
            // If that form of the link is not understood, the plain Default apps page still opens.
            try { System.Diagnostics.Process.Start("ms-settings:defaultapps?registeredAppUser=BrowserSwitch"); }
            catch { try { System.Diagnostics.Process.Start("ms-settings:defaultapps"); } catch { } }
        };
        strip.Controls.Add(text);
        strip.Controls.Add(open);
        return strip;
    }

    // read by --selftest, so the window can be checked without being shown
    public int CategoryCount { get { return categoryList.Items.Count; } }
    public int BrowserCount { get { return browserTree.Nodes.Count; } }
    public int ProfileCount { get { int n = 0; foreach (TreeNode t in browserTree.Nodes) n += t.Nodes.Count; return n; } }
    public int SwitchButtonCount { get { return switchRow.Controls.OfType<Button>().Count(b => b.Name != "shortcuts"); } }
    public string HeaderText { get { return header.Text; } }

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
        categoryList.Items.Clear();
        foreach (var c in Config.Categories)
            categoryList.Items.Add(new Row(c, string.Equals(c.Name, Config.Active, StringComparison.OrdinalIgnoreCase)));
        categoryList.EndUpdate();

        for (int i = 0; i < categoryList.Items.Count; i++)
            if (((Row)categoryList.Items[i]).Cat.Name == keep) categoryList.SelectedIndex = i;
        if (categoryList.SelectedIndex < 0 && categoryList.Items.Count > 0) categoryList.SelectedIndex = 0;

        var live = Config.Current();
        header.Text = live == null
            ? "Nothing set up yet - make a category, then pick a browser for it"
            : "Links open in:   " + Config.Active + "   →   " + live.Shows;

        switchRow.Controls.Clear();
        if (Config.Categories.Count > 0)
            switchRow.Controls.Add(new Label { Text = "Switch to:", AutoSize = true, Padding = new Padding(0, 8, 6, 0) });
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
        var keysButton = new Button { Name = "shortcuts", Text = "Shortcuts…", Height = 32, AutoSize = true,
                                      Padding = new Padding(10, 0, 10, 0), Margin = new Padding(18, 3, 3, 3) };
        keysButton.Click += delegate { EditShortcuts(); };
        switchRow.Controls.Add(keysButton);
        UpdateButtons();
    }

    // one line in the category list
    class Row
    {
        public readonly Category Cat; readonly bool live;
        public Row(Category c, bool isLive) { Cat = c; live = isLive; }
        public override string ToString()
        {
            return (live ? "●  " : "    ") + Cat.Name + "   —   " + (Cat.Shows.Length > 0 ? Cat.Shows : "(not set)") +
                   (Config.ShortcutsOn && Cat.KeyOn && Cat.HotKey.Length > 0 ? "      " + Cat.HotKey : "");
        }
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
