// The part of Browser Switch that lives in the notification area next to the clock - "the dock".
//
//   The Browser Switch icon   click: open the window. Right-click: your categories, Open, Exit.
//   One icon per pinned category (pin it in the window): click it and links switch there, with no
//   window and nothing else opening.
//
// Every switch shows a short note on screen wherever it came from - an icon here, the window,
// --switch, or "Back to normal.cmd" - because the note follows config.txt, not the click.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

class Tray : ApplicationContext
{
    const string OneCopy = @"Local\BrowserSwitch.Dock";          // one dock per signed-in user
    const string OpenSignal = @"Local\BrowserSwitch.OpenWindow";

    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

    // Starts the dock. If one is already running, asks it to open the window, and leaves.
    public static void Run(bool openWindow)
    {
        bool first;
        using (var mutex = new Mutex(true, OneCopy, out first))
        {
            if (!first)
            {
                // this copy was started by a click, so it may bring a window forward - pass that on
                if (openWindow)
                    try { AllowSetForegroundWindow(-1); EventWaitHandle.OpenExisting(OpenSignal).Set(); } catch { }
                return;
            }
            Application.Run(new Tray(openWindow));   // faults go to errors.log - set up in Main
            GC.KeepAlive(mutex);
        }
    }

    readonly Control ui = new Control();                   // lets other threads reach this one
    readonly NotifyIcon main;
    readonly List<NotifyIcon> pinned = new List<NotifyIcon>();
    readonly List<Icon> pinnedIcons = new List<Icon>();
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly FileSystemWatcher watcher;
    readonly System.Windows.Forms.Timer settle = new System.Windows.Forms.Timer { Interval = 250 };
    readonly Note note = new Note();
    readonly HotkeyWindow keys = new HotkeyWindow();
    readonly Dictionary<int, string> keyTargets = new Dictionary<int, string>();   // shortcut id -> category
    SwitchForm window;
    string lastActive, pinnedShape = "", keysShape = "";
    bool keysPaused;

    Tray(bool openWindow)
    {
        ui.CreateControl();
        lastActive = Config.Active;
        // shortcuts were just suggested for an older config.txt: keep them, so they do not change
        if (Config.Suggested) { Config.Suggested = false; Config.Save(); }

        menu.Opening += (s, e) => { FillMenu(); e.Cancel = false; };
        main = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath), ContextMenuStrip = menu, Visible = true };
        main.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenWindow(); };
        Pin();
        Tips();
        keys.Pressed += Pressed;
        RegisterKeys();

        // config.txt also changes from outside: other copies of this program (--switch, --reset) and
        // by hand. Writes come in bursts, so wait a moment for it to settle before reading.
        watcher = new FileSystemWatcher(Config.Dir, "config.txt") { SynchronizingObject = ui };
        FileSystemEventHandler changed = (s, e) => { settle.Stop(); settle.Start(); };
        watcher.Changed += changed;
        watcher.Created += changed;
        watcher.Renamed += (s, e) => { settle.Stop(); settle.Start(); };
        watcher.EnableRaisingEvents = true;
        settle.Tick += (s, e) => { settle.Stop(); FileChanged(); };

        // a copy started from the Start menu or the Desktop asks this one to open the window
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSignal);
        new Thread(() => { while (signal.WaitOne()) ui.BeginInvoke((MethodInvoker)OpenWindow); }) { IsBackground = true }.Start();

        if (openWindow) OpenWindow();
    }

    void OpenWindow()
    {
        if (window == null || window.IsDisposed)
        {
            window = new SwitchForm();
            window.Saved = Refresh;                          // its own changes reach the dock at once
            window.PauseShortcuts = PauseKeys;
            window.ShortcutFree = keys.IsFree;
        }
        window.Show();
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
        window.Activate();
    }

    void SwitchTo(Category c)
    {
        if (c == null || c.Exe.Length == 0) return;
        bool already = string.Equals(Config.Active, c.Name, StringComparison.OrdinalIgnoreCase);
        Config.Active = c.Name;
        Config.Save();
        if (window != null && !window.IsDisposed) window.Reload();
        Refresh();
        // nothing changed, but a key was pressed or an icon clicked: still confirm it
        if (already) using (var icon = IconFor(c)) note.Say("Links already open in:  " + c.Name, c.Shows, icon);
    }

    // ---- keyboard shortcuts ---------------------------------------------------------------------

    string KeysShape()
    {
        return (Config.ShortcutsOn ? "on" : "off") + "|" + (Config.NextOn ? Config.NextKey : "") + "|" +
               (Config.PrevOn ? Config.PrevKey : "") + "|" +
               string.Join("|", Config.Categories.Select(c => c.Name + "=" + (c.KeyOn ? c.HotKey : "")));
    }

    // Takes the shortcuts from Windows - only while they are turned on, and not while the Shortcuts
    // window is recording a new one (pressing it would switch instead of being recorded).
    void RegisterKeys()
    {
        keys.Clear();
        keyTargets.Clear();
        keysShape = KeysShape();
        if (!Config.ShortcutsOn || keysPaused) return;
        if (Config.NextOn && Config.NextKey.Length > 0) keys.Add(1, Config.NextKey);
        if (Config.PrevOn && Config.PrevKey.Length > 0) keys.Add(2, Config.PrevKey);
        int id = 100;
        foreach (var c in Config.Categories)
        {
            if (c.KeyOn && c.HotKey.Length > 0 && keys.Add(id, c.HotKey)) keyTargets[id] = c.Name;
            id++;
        }
    }

    void PauseKeys(bool pause)
    {
        keysPaused = pause;
        RegisterKeys();
    }

    void Pressed(int id)
    {
        if (id == 1) Step(1);
        else if (id == 2) Step(-1);
        else { string name; if (keyTargets.TryGetValue(id, out name)) SwitchTo(Find(name)); }
    }

    // Next or previous among the categories ticked for it (and with a browser), in list order,
    // wrapping round. It moves on from wherever the live one sits in the list - even when the live
    // one is not ticked itself, so a category reached by its own key never traps you.
    void Step(int by)
    {
        var c = StepFrom(Config.Categories, Config.Active, by);
        if (c != null) SwitchTo(c);
        else note.Say("Nothing to step through", "Tick categories for next / previous under Shortcuts…", null);
    }

    public static Category StepFrom(List<Category> all, string active, int by)
    {
        int n = all.Count;
        int at = all.FindIndex(c => string.Equals(c.Name, active, StringComparison.OrdinalIgnoreCase));
        for (int i = 1; i <= n; i++)
        {
            int j = at < 0 ? (by > 0 ? i - 1 : n - i) : ((at + by * i) % n + n) % n;
            if (all[j].InCycle && all[j].Exe.Length > 0) return all[j];
        }
        return null;
    }

    void FileChanged()
    {
        string text;
        try { text = File.ReadAllText(Config.File_); } catch { return; }   // mid-write: another event follows
        if (text == Config.LastText) return;                                 // what this program wrote itself
        if (!Config.Load()) return;
        if (window != null && !window.IsDisposed) window.Reload();
        Refresh();
    }

    // Config in memory is current: bring the dock up to date, and if the live category changed, say so.
    void Refresh()
    {
        if (Shape() != pinnedShape) Pin();
        if (KeysShape() != keysShape) RegisterKeys();
        Tips();
        if (string.Equals(Config.Active, lastActive, StringComparison.OrdinalIgnoreCase)) return;
        lastActive = Config.Active;
        var live = Config.Current();
        if (live != null) using (var icon = IconFor(live)) note.Say("Links now open in:  " + live.Name, live.Shows, icon);
        else note.Say("No category is live", "Links open in your original browser", null);
    }

    string Shape()
    {
        return string.Join("\n", Config.Categories.Where(c => c.InDock).Select(c => c.Name + "|" + c.Exe + "|" + c.Icon));
    }

    void Pin()
    {
        foreach (var n in pinned) { n.Visible = false; n.Dispose(); }
        foreach (var i in pinnedIcons) i.Dispose();
        pinned.Clear(); pinnedIcons.Clear();
        foreach (var c in Config.Categories.Where(x => x.InDock))
        {
            string name = c.Name;
            var icon = IconFor(c);
            pinnedIcons.Add(icon);
            var n = new NotifyIcon { Icon = icon, ContextMenuStrip = menu, Tag = name, Visible = true };
            n.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) SwitchTo(Find(name)); };
            pinned.Add(n);
        }
        pinnedShape = Shape();
    }

    void Tips()
    {
        var live = Config.Current();
        main.Text = Cut("Browser Switch - " + (live != null ? "links open in " + live.Name : "no category live"));
        foreach (var n in pinned)
        {
            var c = Find((string)n.Tag);
            if (c != null) n.Text = TipFor(c);
        }
    }

    // What a pinned icon says under the mouse - set from its right-click menu.
    public static string TipFor(Category c)
    {
        return Cut(Config.TipSwitchTo ? "Switch to " + c.Name : c.Name);
    }

    void SetTips(bool switchTo)
    {
        Config.TipSwitchTo = switchTo;
        Config.Save();
        Tips();
    }

    static string Cut(string s) { return s.Length <= 63 ? s : s.Substring(0, 62) + "…"; }   // Windows' limit

    static Category Find(string name)
    {
        return Config.Categories.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    void FillMenu()
    {
        menu.Items.Clear();
        foreach (var c in Config.Categories)
        {
            string name = c.Name;
            var item = new ToolStripMenuItem(c.Name + (c.Shows.Length > 0 ? "     " + c.Shows : ""))
            {
                Checked = string.Equals(c.Name, Config.Active, StringComparison.OrdinalIgnoreCase),
                Enabled = c.Exe.Length > 0,
                ShortcutKeyDisplayString = Config.ShortcutsOn && c.KeyOn ? c.HotKey : ""
            };
            item.Click += (s, e) => SwitchTo(Find(name));
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new ToolStripSeparator());
        var example = Config.Categories.FirstOrDefault(c => c.InDock);
        if (example != null)
        {
            var tips = new ToolStripMenuItem("Hover text on dock icons");
            var justName = new ToolStripMenuItem(example.Name) { Checked = !Config.TipSwitchTo };
            var switchTo = new ToolStripMenuItem("Switch to " + example.Name) { Checked = Config.TipSwitchTo };
            justName.Click += (s, e) => SetTips(false);
            switchTo.Click += (s, e) => SetTips(true);
            tips.DropDownItems.Add(justName);
            tips.DropDownItems.Add(switchTo);
            menu.Items.Add(tips);
        }
        menu.Items.Add("Open Browser Switch", null, (s, e) => OpenWindow());
        menu.Items.Add("Exit", null, (s, e) => Exit());
    }

    void Exit()
    {
        watcher.EnableRaisingEvents = false;
        if (window != null && !window.IsDisposed) window.Close();
        main.Visible = false; main.Dispose();
        foreach (var n in pinned) { n.Visible = false; n.Dispose(); }
        foreach (var i in pinnedIcons) i.Dispose();
        keys.Dispose();
        note.Close();
        ExitThread();
    }

    // ---- icons for pinned categories ------------------------------------------------------------

    // The browser's own icon, the browser's icon recoloured, a colour with the category's first
    // letter, or an image file you picked. Anything that fails to load becomes a grey letter rather
    // than no icon at all.
    public static Icon IconFor(Category c)
    {
        string spec = c.Icon ?? "";
        try
        {
            if (spec.StartsWith("color:", StringComparison.OrdinalIgnoreCase))
                return Letter(c.Name, ColorTranslator.FromHtml(spec.Substring(6)));
            if (spec.StartsWith("tint:", StringComparison.OrdinalIgnoreCase) && c.Exe.Length > 0 && File.Exists(c.Exe))
            {
                int[] v = spec.Substring(5).Split(',').Select(x => int.Parse(x.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                using (var own = BrowserIcon(c))
                using (var src = own.ToBitmap())
                using (var tinted = Recolour(src, v[0], v[1], v[2]))
                    return Owned(tinted);
            }
            if (spec.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return FromFile(spec.Substring(5));
            if (c.Exe.Length > 0 && File.Exists(c.Exe))
                return BrowserIcon(c);
        }
        catch { }
        return Letter(c.Name, Color.Gray);
    }

    // The browser's icon as that profile shows it: for Brave, Chrome and Edge the one with the
    // profile's picture on it, which the browser keeps in the profile's folder; otherwise the plain
    // icon from the exe (Firefox has no per-profile icons).
    static Icon BrowserIcon(Category c)
    {
        string own = Machine.ProfileIcon(c.Exe, c.Args);
        if (own != null) try { return new Icon(own, 32, 32); } catch { }
        return Icon.ExtractAssociatedIcon(c.Exe);
    }

    static Icon FromFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".ico") return new Icon(path, 32, 32);
        if (ext == ".exe") return Icon.ExtractAssociatedIcon(path);
        using (var img = Image.FromFile(path))
        using (var bmp = new Bitmap(32, 32))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, 0, 0, 32, 32);
            }
            return Owned(bmp);
        }
    }

    static Icon Letter(string name, Color color)
    {
        using (var bmp = new Bitmap(32, 32))
        {
            using (var g = Graphics.FromImage(bmp))
            using (var fill = new SolidBrush(color))
            using (var font = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var ink = new SolidBrush((color.R * 299 + color.G * 587 + color.B * 114) / 1000 > 160 ? Color.Black : Color.White))
            using (var centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.FillEllipse(fill, 1, 1, 30, 30);
                string letter = name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?";
                g.DrawString(letter, font, ink, new RectangleF(0, 1, 32, 32), centre);
            }
            return Owned(bmp);
        }
    }

    // The same picture with its colours moved round the colour wheel (hue, in degrees), made
    // greyer or more vivid (strength, in percent: 100 = as it was), and darker or lighter
    // (brightness, -50 to +50). Transparency is kept, so the shape stays the browser's own.
    public static Bitmap Recolour(Bitmap src, int hue, int strength, int brightness)
    {
        var bmp = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                Color p = src.GetPixel(x, y);
                if (p.A == 0) continue;
                double h = (p.GetHue() + hue + 360) % 360;
                double s = Math.Min(1.0, p.GetSaturation() * strength / 100.0);
                double l = Math.Max(0.0, Math.Min(1.0, p.GetBrightness() + brightness / 100.0));
                bmp.SetPixel(x, y, FromHsl(p.A, h, s, l));
            }
        return bmp;
    }

    static Color FromHsl(int alpha, double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s, hp = h / 60.0, x = c * (1 - Math.Abs(hp % 2 - 1)), m = l - c / 2;
        double r = 0, g = 0, b = 0;
        if (hp < 1) { r = c; g = x; } else if (hp < 2) { r = x; g = c; } else if (hp < 3) { g = c; b = x; }
        else if (hp < 4) { g = x; b = c; } else if (hp < 5) { r = x; b = c; } else { r = c; b = x; }
        return Color.FromArgb(alpha, Byte(r + m), Byte(g + m), Byte(b + m));
    }

    static int Byte(double v) { return Math.Max(0, Math.Min(255, (int)Math.Round(v * 255))); }

    // An Icon that owns its own copy of the picture, so the temporary handle can go at once.
    static Icon Owned(Bitmap bmp)
    {
        IntPtr h = bmp.GetHicon();
        try { using (var temp = Icon.FromHandle(h)) return (Icon)temp.Clone(); }
        finally { DestroyIcon(h); }
    }
}

// The short message on screen after a switch. It never takes the keyboard focus, has no taskbar
// button, lets clicks pass straight through, and fades by itself - it must not get in the way.
//
// It is drawn as one picture - a dark rounded box whose edges fade smoothly into see-through - and
// handed to Windows pixel for pixel (a "layered" window). The first version cut its corners with a
// hard-edged mask instead, which left jagged light pixels round the rim.
class Note : Form
{
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT dst, ref SIZE size,
        IntPtr hdcSrc, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER info, uint usage,
        out IntPtr bits, IntPtr section, uint offset);

    readonly Font titleFont = new Font("Segoe UI", 12F, FontStyle.Bold);
    readonly Font detailFont = new Font("Segoe UI", 9.5F);
    readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer { Interval = 40 };
    Bitmap picture;   // exactly what is on screen
    int wait;         // ticks left before it starts to fade
    int alpha;        // 255 = fully there, 0 = gone

    public Note()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        clock.Tick += delegate
        {
            if (--wait > 0) return;
            alpha -= 20;
            if (alpha > 0) { Push(); return; }
            clock.Stop();
            Hide();
        };
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00080000   // layered: shown as a picture with its own see-through edges
                        | 0x08000000   // never activated: focus stays where it was
                        | 0x00000080   // tool window: no taskbar button, not in Alt+Tab
                        | 0x00000020;  // clicks pass through to whatever is underneath
            return cp;
        }
    }

    // icon: shown in front of the second line - the category's dock icon - or null for none
    public void Say(string head, string more, Icon icon)
    {
        Lay(head, more, icon);
        alpha = 255;
        wait = 45;   // 45 x 40 ms = about 1.8 seconds, then it fades
        if (!IsHandleCreated) CreateHandle();
        Push();
        if (!Visible) Show();
        clock.Stop();
        clock.Start();
    }

    // Draw it for this text and place it, without showing it - the tests look at Picture.
    // Whether there is a second line is decided from the text itself: asking a control inside a
    // window not yet on screen whether it is visible always says no, which once cut that line off.
    internal void Lay(string head, string more, Icon icon)
    {
        more = more ?? "";
        bool two = more.Length > 0;
        const int iconSize = 16;
        int indent = two && icon != null ? iconSize + 6 : 0;   // the second line starts after the icon
        SizeF t, d = SizeF.Empty;
        using (var probe = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(probe))
        {
            t = g.MeasureString(head, titleFont);
            if (two) d = g.MeasureString(more, detailFont);
        }
        int w = Math.Max((int)Math.Ceiling(Math.Max(t.Width, indent + d.Width)) + 32, 220);
        int h = (int)Math.Ceiling(12 + t.Height + (two ? Math.Max(d.Height, iconSize) + 1 : 0) + 12);

        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        using (var path = new GraphicsPath())
        using (var fill = new SolidBrush(Color.FromArgb(245, 30, 30, 34)))
        using (var ink = new SolidBrush(Color.White))
        using (var grey = new SolidBrush(Color.FromArgb(200, 200, 205)))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float r = 22;
            path.AddArc(0, 0, r, r, 180, 90);
            path.AddArc(w - r, 0, r, r, 270, 90);
            path.AddArc(w - r, h - r, r, r, 0, 90);
            path.AddArc(0, h - r, r, r, 90, 90);
            path.CloseFigure();
            g.FillPath(fill, path);
            g.DrawString(head, titleFont, ink, 16, 12);
            if (two)
            {
                float y2 = 12 + t.Height + 1;
                if (icon != null)
                    using (var small = icon.ToBitmap())
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(small, new RectangleF(18, y2 + (d.Height - iconSize) / 2f, iconSize, iconSize));
                    }
                g.DrawString(more, detailFont, grey, 16 + indent, y2);
            }
        }
        if (picture != null) picture.Dispose();
        picture = bmp;

        // bottom-centre of the screen you are working on, just above the taskbar
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Bounds = new Rectangle(area.Left + (area.Width - w) / 2, area.Bottom - h - 40, w, h);
    }

    internal Bitmap Picture { get { return picture; } }

    // Hands the picture to Windows at the current fade. The pixels are copied as they are -
    // "premultiplied", the form a see-through edge must be in, or it shows up too light.
    void Push()
    {
        if (picture == null) return;
        int w = picture.Width, h = picture.Height;
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bits;
        var info = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        IntPtr dib = CreateDIBSection(screen, ref info, 0, out bits, IntPtr.Zero, 0);
        var data = picture.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly,
                                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var row = new byte[w * 4];
        for (int y = 0; y < h; y++)
        {
            Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
            Marshal.Copy(row, 0, bits + y * w * 4, row.Length);
        }
        picture.UnlockBits(data);
        IntPtr old = SelectObject(mem, dib);
        try
        {
            var at = new POINT { X = Left, Y = Top };
            var size = new SIZE { CX = w, CY = h };
            var from = new POINT();
            var blend = new BLENDFUNCTION { SourceConstantAlpha = (byte)Math.Max(0, Math.Min(255, alpha)), AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screen, ref at, ref size, mem, ref from, 0, ref blend, 2);   // ULW_ALPHA
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(dib);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
