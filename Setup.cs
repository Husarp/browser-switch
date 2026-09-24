// The setup screen: what the window shows on the very first start, and again whenever Browser Switch
// is not the default browser.
//
// Three steps. The first says how Browser Switch works and why it can be trusted - what it needs,
// that it works offline, what the internet is used for. The second asks for three choices: link
// cleaning, the link log, and asking GitHub for updates. The third is the one thing Windows leaves to
// the person at the computer: making it the default browser - or, if it already is, says so. Then a
// big "You're all set", which also makes a first category from the browser used so far if there is
// none, and Finish.
//
// Buttons as in any Windows setup: Back and Skip on the left, the blue button that goes on on the
// right.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

partial class SwitchForm
{
    Panel setup;
    FlowLayoutPanel setupHow, setupChoices, setupDefault, setupDone;
    Control setupTodo, setupAlready;      // step 3: the steps to take, or "already done"
    Button setupGo;                       // step 3's blue button: Open Windows Settings, or Next
    Label setupStatus, setupNext;
    CheckBox chooseClean, chooseLog, chooseUpdates;
    bool madeFirstCategory;
    readonly Timer setupWatch = new Timer { Interval = 1500 };
    readonly List<Control> mainScreen = new List<Control>();   // hidden while the setup screen shows

    const int SetupWidth = 640;
    static readonly Color Done = Color.FromArgb(16, 124, 65);

    // Shows the setup screen over everything else, on its first step. Called when the window opens
    // on a first start or while Browser Switch is not the default browser; also from About & updates.
    public void ShowSetup()
    {
        if (setup == null) BuildSetup();
        foreach (var c in mainScreen) c.Visible = false;
        chooseClean.Checked = Config.CleanOn && Config.UnwrapOn;
        chooseLog.Checked = Config.LogOn;
        chooseUpdates.Checked = Config.UpdateCheck;
        SetupStep(1);
        setup.Visible = true;
    }

    // One of the three steps - the third starts watching for Windows to report the change - or, as
    // step 4, "You're all set".
    void SetupStep(int step)
    {
        setupHow.Visible = step == 1;
        setupChoices.Visible = step == 2;
        setupDefault.Visible = step == 3;
        setupDone.Visible = step == 4;
        setup.AutoScrollPosition = new Point(0, 0);
        setupStatus.ForeColor = SystemColors.GrayText;
        setupStatus.Text = "";
        if (step == 3)
        {
            bool already = IsDefaultBrowser;
            setupTodo.Visible = !already;
            setupAlready.Visible = already;
            setupGo.Text = already ? "Next  →" : "Open Windows Settings";
        }
        if (step == 4)
        {
            madeFirstCategory = MakeFirstCategory();
            setupNext.Text = WhatNext();
        }
        if (step == 3 && !IsDefaultBrowser) setupWatch.Start(); else setupWatch.Stop();
    }

    // The three choices of the second step, as they are ticked.
    void KeepChoices()
    {
        Config.CleanOn = Config.UnwrapOn = chooseClean.Checked;
        Config.LogOn = chooseLog.Checked;
        Config.UpdateCheck = chooseUpdates.Checked;
        Save();
    }

    // Back to the main screen - on Finish, or Skip. The setup has been seen: it comes back by itself
    // only while Browser Switch is not the default browser.
    void LeaveSetup()
    {
        setupWatch.Stop();
        if (setup != null) setup.Visible = false;
        foreach (var c in mainScreen) c.Visible = true;
        if (!Config.SetupDone) { Config.SetupDone = true; Save(); }
        Reload();
        CheckDefault();
    }

    // With no category yet, the browser used until now becomes the first one - live - so links keep
    // going where they went, and the list shows where that is. True if one was made.
    bool MakeFirstCategory()
    {
        if (Config.Categories.Count > 0) return false;
        string exe = Config.FallbackExe;
        if (exe.Length == 0 || !File.Exists(exe))
        {
            var first = Machine.Browsers().FirstOrDefault();
            if (first == null) return false;
            exe = first.Exe;
        }
        var browser = Machine.Browsers().FirstOrDefault(b => string.Equals(b.Exe, exe, StringComparison.OrdinalIgnoreCase));
        string full = browser != null ? browser.Name : Path.GetFileNameWithoutExtension(exe);
        string name = full;
        foreach (string maker in new[] { "Mozilla ", "Google ", "Microsoft " })   // "Firefox", not "Mozilla Firefox"
            if (name.StartsWith(maker, StringComparison.OrdinalIgnoreCase) && name.Length > maker.Length) name = name.Substring(maker.Length);
        string key = Config.SuggestKey(name, 1);
        Config.Categories.Add(new Category { Name = name, Exe = exe, Args = "", Shows = full, HotKey = key, DefaultKey = key });
        Config.Active = name;
        Save();
        return true;
    }

    void BuildSetup()
    {
        setup = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Window, AutoScroll = true, Visible = false };
        setupHow = Column();
        setupChoices = Column();
        setupDefault = Column();
        setupDone = Column();
        setup.Controls.Add(setupHow);
        setup.Controls.Add(setupChoices);
        setup.Controls.Add(setupDefault);
        setup.Controls.Add(setupDone);
        setup.Resize += delegate
        {
            int left = Math.Max(16, (setup.ClientSize.Width - SetupWidth) / 2);
            setupHow.Left = setupChoices.Left = setupDefault.Left = setupDone.Left = left;
        };

        // ---- step 1: how it works ----
        setupHow.Controls.Add(Title("Welcome to Browser Switch", "Step 1 of 3 - how it works"));
        setupHow.Controls.Add(Heading("One step is needed - here is why"));
        setupHow.Controls.Add(Text_(
            "Browser Switch decides which browser - and which profile of it - each link opens in. For that, every link " +
            "has to pass through it first, and Windows sends links only to your default browser. So Browser Switch has " +
            "to be your default browser; it then hands each link straight on to the browser you chose. This is the " +
            "only way it can work.\n" +
            "Windows lets only you make this choice - no program and no command may make it for you. That protects " +
            "you from programs that would take over your browser. Step 3 shows you where."));

        setupHow.Controls.Add(Heading("Why you can trust it"));
        var trust = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Color.FromArgb(243, 247, 252),
                                           Padding = new Padding(12, 8, 12, 2), Margin = new Padding(0, 0, 0, 10),
                                           Width = SetupWidth, MaximumSize = new Size(SetupWidth, 0) };
        trust.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
        trust.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Trust(trust, "Fully offline", "Everything works without the internet. The only time Browser Switch goes online is " +
              "to ask GitHub for a newer version - and only if you allow it in the next step.");
        Trust(trust, "Sends nothing", "No account, no ads, no tracking, nothing collected. Your links, settings and the " +
              "link log stay in its own folder on this PC.");
        Trust(trust, "Made for personal use", "A small project made for private use, and shared freely - nothing is sold, " +
              "nothing is in it for anyone else.");
        Trust(trust, "Open", "Every file is public on GitHub, to read or to build yourself - what runs is what you can see.");
        Trust(trust, "Easy to undo", "Your other browsers stay as they are. Make one of them the default again at any " +
              "time, or uninstall Browser Switch.");
        setupHow.Controls.Add(trust);
        var next = PrimaryButton("Next  →");
        next.Click += delegate { SetupStep(2); };
        setupHow.Controls.Add(NavRow(next, Link("Skip setup", LeaveSetup)));

        // ---- step 2: three choices ----
        setupChoices.Controls.Add(Title("Your choices", "Step 2 of 3 - all of these can be changed later, on their tabs"));
        chooseClean = Choice(setupChoices, "Clean links", "Takes tracking out of links - utm_source, fbclid, YouTube's si... - and " +
                             "skips redirects such as google.com/url?q=..., so the page opens directly. Only parts known to be " +
                             "tracking are removed, so links keep working.");
        chooseLog = Choice(setupChoices, "Keep a log of links", "Which app each link came from, where it opened, and what was " +
                           "changed - kept on this PC only, the newest " + LinkLog.Keep + " links.");
        chooseUpdates = Choice(setupChoices, "Check for updates automatically", "Once a day, ask GitHub whether a newer version " +
                               "exists - the only time Browser Switch goes online. Left off, it asks only when you press Check now " +
                               "on the About & updates tab. Nothing is downloaded until you choose to update.");
        var next2 = PrimaryButton("Next  →");
        next2.Click += delegate { KeepChoices(); SetupStep(3); };
        setupChoices.Controls.Add(NavRow(next2, Link("←  Back", () => SetupStep(1))));

        // ---- step 3: making it the default browser - or saying it already is ----
        setupDefault.Controls.Add(Title("Make Browser Switch your default browser", "Step 3 of 3 - the one step Windows leaves to you"));
        var todo = Column();
        todo.Top = 0; todo.Margin = new Padding(0);
        todo.Controls.Add(Text_(
            "1.  Press Open Windows Settings below - it opens on Browser Switch's own page.\n" +
            "2.  Windows 11: press Set default at the top of that page.\n" +
            "     Windows 10: under Web browser, click the browser shown and choose Browser Switch.\n" +
            "3.  Come back here - this screen notices by itself and takes you on."));
        todo.Controls.Add(Text_(
            "Why not automatically? Windows keeps the default browser as a choice only you can make: a program - or a " +
            "command typed in PowerShell - that tries to set it is simply ignored. That is what stops other programs " +
            "from taking over your browser, and why this one click is yours."));
        setupTodo = todo;
        setupDefault.Controls.Add(todo);
        setupAlready = Banner("✓", "Already done", "Browser Switch is already your default browser - there is nothing to do here.");
        setupDefault.Controls.Add(setupAlready);
        setupGo = PrimaryButton("Open Windows Settings");
        setupGo.Click += delegate
        {
            if (IsDefaultBrowser) { SetupStep(4); return; }
            OpenDefaultAppsSettings();
            setupStatus.ForeColor = SystemColors.GrayText;
            setupStatus.Text = "Waiting for Windows…";
        };
        setupDefault.Controls.Add(NavRow(setupGo, Link("←  Back", () => SetupStep(2)), Link("Skip for now", LeaveSetup)));
        setupStatus = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(2, 10, 0, 12) };
        setupDefault.Controls.Add(setupStatus);

        // ---- you're all set ----
        setupDone.Controls.Add(Banner("✓", "You're all set!", "Browser Switch is your default browser."));
        setupDone.Controls.Add(Text_("From now on every link passes through Browser Switch, and goes on to the browser you choose."));
        setupNext = Text_("");
        setupDone.Controls.Add(setupNext);
        var finish = PrimaryButton("Finish");
        finish.Click += delegate { LeaveSetup(); };
        setupDone.Controls.Add(NavRow(finish));

        setupWatch.Tick += delegate
        {
            if (!IsDefaultBrowser) return;
            setupWatch.Stop();
            SetupStep(4);
        };
        FormClosed += delegate { setupWatch.Stop(); };

        Controls.Add(setup);
        setup.BringToFront();
    }

    // The "all set" page's advice: what happens to links now, and what to do first.
    string WhatNext()
    {
        var live = Config.Current();
        if (madeFirstCategory && live != null)
            return "Your first category, " + live.Name + ", is ready and live: links open in " + live.Shows + ", just as before.\n" +
                   "Next, on the Categories tab: press New category, give it a name - Work, Home, School - pick a browser " +
                   "profile on the right and press Use this. Then switch between categories with one click: here, from the " +
                   "dock next to the clock, or with a keyboard shortcut.";
        if (live != null)
            return "Links open in " + live.Name + " (" + live.Shows + "). Switch categories here, from the dock next to the clock, " +
                   "or with a keyboard shortcut.";
        return "Until a category is live, links go to " + Program.OriginalBrowserName() + ", as before. Make one on the " +
               "Categories tab: press New category, pick a browser profile on the right and press Use this.";
    }

    static FlowLayoutPanel Column()
    {
        return new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
                                     AutoSizeMode = AutoSizeMode.GrowAndShrink, Top = 14, Left = 16 };
    }

    Control Title(string title, string under)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
        row.Controls.Add(new PictureBox { Image = Icon.ToBitmap(), Size = new Size(40, 40), SizeMode = PictureBoxSizeMode.Zoom,
                                          Margin = new Padding(0, 2, 12, 0) });
        var words = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        words.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 16F, FontStyle.Bold), Margin = new Padding(0) });
        words.Controls.Add(new Label { Text = under, AutoSize = true, ForeColor = SystemColors.GrayText, Font = new Font("Segoe UI", 10F),
                                       Margin = new Padding(2, 0, 0, 0) });
        row.Controls.Add(words);
        return row;
    }

    // A big green tick with a headline and a line under it - for "done".
    static Control Banner(string mark, string headline, string under)
    {
        var box = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.FromArgb(232, 246, 237),
                                        Padding = new Padding(14, 10, 20, 10), Margin = new Padding(0, 4, 0, 14),
                                        MinimumSize = new Size(SetupWidth, 0) };
        box.Controls.Add(new Label { Text = mark, AutoSize = true, Font = new Font("Segoe UI", 30F, FontStyle.Bold), ForeColor = Done,
                                     Margin = new Padding(0, 0, 14, 0) });
        var words = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        words.Controls.Add(new Label { Text = headline, AutoSize = true, Font = new Font("Segoe UI", 18F, FontStyle.Bold), ForeColor = Done,
                                       Margin = new Padding(0) });
        words.Controls.Add(new Label { Text = under, AutoSize = true, MaximumSize = new Size(SetupWidth - 110, 0), Font = new Font("Segoe UI", 10.5F),
                                       Margin = new Padding(2, 0, 0, 0) });
        box.Controls.Add(words);
        return box;
    }

    static Label Heading(string text)
    {
        return new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 11F, FontStyle.Bold), Margin = new Padding(0, 2, 0, 4) };
    }

    static Label Text_(string text)
    {
        return new Label { Text = text, AutoSize = true, MaximumSize = new Size(SetupWidth, 0), Font = new Font("Segoe UI", 9.75F), UseMnemonic = false,
                           Margin = new Padding(0, 0, 0, 10) };
    }

    static void Trust(TableLayoutPanel table, string what, string why)
    {
        table.Controls.Add(new Label { Text = "✓  " + what, AutoSize = true, Font = new Font("Segoe UI", 9.75F, FontStyle.Bold),
                                       ForeColor = Color.FromArgb(0, 90, 158), Margin = new Padding(0, 0, 8, 6) });
        table.Controls.Add(new Label { Text = why, AutoSize = true, MaximumSize = new Size(SetupWidth - 200, 0),
                                       Font = new Font("Segoe UI", 9.75F), Margin = new Padding(0, 0, 0, 6) });
    }

    // A tick with its name in bold, and what it means underneath.
    static CheckBox Choice(Control column, string what, string means)
    {
        var tick = new CheckBox { Text = what, AutoSize = true, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold), Margin = new Padding(0, 6, 0, 0) };
        column.Controls.Add(tick);
        column.Controls.Add(new Label { Text = means, AutoSize = true, MaximumSize = new Size(SetupWidth - 20, 0), UseMnemonic = false,
                                        Font = new Font("Segoe UI", 9.75F), ForeColor = Color.FromArgb(70, 70, 70),
                                        Margin = new Padding(20, 0, 0, 12) });
        return tick;
    }

    static Button PrimaryButton(string text)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 34, Padding = new Padding(14, 0, 14, 0),
                             Font = new Font("Segoe UI", 9.75F, FontStyle.Bold), BackColor = Ui.Accent, ForeColor = Color.White,
                             FlatStyle = FlatStyle.Flat };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    static LinkLabel Link(string text, Action click)
    {
        var l = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 10, 22, 0) };
        l.LinkClicked += delegate { click(); };
        return l;
    }

    // The bottom row of a step: Back / Skip on the left, the blue button on the right.
    static Control NavRow(Button primary, params Control[] left)
    {
        var row = new Panel { Width = SetupWidth, Height = 40, Margin = new Padding(0, 8, 0, 0) };
        var links = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        links.Controls.AddRange(left);
        primary.Dock = DockStyle.Right;
        row.Controls.Add(links);
        row.Controls.Add(primary);
        return row;
    }

    // Windows 11 can open straight at one app's page, which saves hunting through the list. The name
    // has to be the value name under RegisteredApplications - "BrowserSwitch", no space. If that form
    // of the link is not understood, the plain Default apps page still opens.
    static void OpenDefaultAppsSettings()
    {
        try { System.Diagnostics.Process.Start("ms-settings:defaultapps?registeredAppUser=BrowserSwitch"); }
        catch { try { System.Diagnostics.Process.Start("ms-settings:defaultapps"); } catch { } }
    }
}
