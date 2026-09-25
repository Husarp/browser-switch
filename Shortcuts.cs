// Keyboard shortcuts for switching: one per category, plus "next" and "previous" that step through
// every category in order. The dock (Tray.cs) holds them, so they work anywhere while it runs.
//
// A shortcut is written the way you would say it: "Ctrl+Alt+1", "Ctrl+Alt+Shift+Space".

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

static class Shortcut
{
    public const uint Alt = 1, Ctrl = 2, Shift = 4, Win = 8;   // Windows' own modifier values

    [DllImport("user32.dll")] static extern int ToUnicodeEx(uint vk, uint scan, byte[] state,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder buf, int size, uint flags, IntPtr layout);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyEx(uint code, uint type, IntPtr layout);
    [DllImport("user32.dll")] static extern short GetKeyState(int vk);

    public static string Format(bool ctrl, bool alt, bool shift, bool win, Keys key)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");
        if (key >= Keys.D0 && key <= Keys.D9) parts.Add(((int)(key - Keys.D0)).ToString());
        else if (key >= Keys.NumPad0 && key <= Keys.NumPad9) parts.Add("Num" + (int)(key - Keys.NumPad0));
        // these keys have two names inside Windows ("Next" is PageDown); say the one on the key
        else if (key == Keys.PageDown) parts.Add("PageDown");
        else if (key == Keys.PageUp) parts.Add("PageUp");
        else if (key == Keys.Enter) parts.Add("Enter");
        else if (key == Keys.PrintScreen) parts.Add("PrintScreen");
        else if (key == Keys.CapsLock) parts.Add("CapsLock");
        else parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Is this combination free - held by no other program? Asks Windows by taking it for a moment.
    // No window is needed for that (the thread itself can hold it), and none must be made: this runs
    // while config.txt is read, before the program is allowed any window.
    public static bool Free(string text)
    {
        uint mods; Keys key;
        if (!TryParse(text, out mods, out key)) return false;
        if (!RegisterHotKey(IntPtr.Zero, 0xB5, mods | 0x4000, (uint)key)) return false;
        UnregisterHotKey(IntPtr.Zero, 0xB5);
        return true;
    }

    public static bool TryParse(string text, out uint mods, out Keys key)
    {
        mods = 0; key = Keys.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] parts = text.Split('+');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            string p = parts[i].Trim().ToLowerInvariant();
            if (p == "ctrl") mods |= Ctrl;
            else if (p == "alt") mods |= Alt;
            else if (p == "shift") mods |= Shift;
            else if (p == "win") mods |= Win;
            else return false;
        }
        string last = parts[parts.Length - 1].Trim();
        if (last.Length == 1 && char.IsDigit(last[0])) key = Keys.D0 + (last[0] - '0');
        else if (last.Length == 4 && last.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && char.IsDigit(last[3]))
            key = Keys.NumPad0 + (last[3] - '0');
        else if (!Enum.TryParse(last, true, out key)) return false;
        return key != Keys.None;   // any key, with or without Ctrl, Alt, Shift or Win
    }

    public static bool WinDown() { return GetKeyState(0x5B) < 0 || GetKeyState(0x5C) < 0; }

    // What the combination types, if anything. A shortcut takes its keys away from every program
    // while it is on, so this is worth knowing - though the choice stays yours. Two cases type: a
    // key on its own or with Shift (K types "k"), and on keyboards with an AltGr key - Polish among
    // them - Ctrl+Alt+key, because AltGr IS Ctrl+Alt: Ctrl+Alt+A types "ą". Windows is asked, for
    // the keyboard in use. Returns what it types, or null.
    public static string TypesCharacter(string text)
    {
        uint mods; Keys key;
        if (!TryParse(text, out mods, out key)) return null;
        bool ctrl = (mods & Ctrl) != 0, alt = (mods & Alt) != 0, shift = (mods & Shift) != 0;
        if ((mods & Win) != 0) return null;
        if (!ctrl && !alt) return Typed(key, false, shift);
        if (!ctrl || !alt) return null;
        string altGr = Typed(key, true, shift);
        return altGr == null || altGr == Typed(key, false, shift) ? null : altGr;
    }

    static string Typed(Keys key, bool ctrlAlt, bool shift)
    {
        var state = new byte[256];
        if (ctrlAlt) { state[0x11] = state[0x12] = state[0xA2] = state[0xA5] = 0x80; }   // Ctrl, Alt, left Ctrl, right Alt
        if (shift) state[0x10] = 0x80;
        IntPtr layout = GetKeyboardLayout(0);
        var buf = new StringBuilder(8);
        // flag 4: look only - do not disturb an accent the person may be halfway through typing
        int n = ToUnicodeEx((uint)key, MapVirtualKeyEx((uint)key, 0, layout), state, buf, buf.Capacity, 4, layout);
        if (n < 0) return "(an accent)";
        if (n == 0) return null;
        string s = buf.ToString(0, Math.Min(n, buf.Length));
        return s.Length > 0 && !char.IsControl(s[0]) ? s : null;
    }
}

// A window nobody sees, which Windows tells whenever a registered shortcut is pressed.
class HotkeyWindow : NativeWindow, IDisposable
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    const uint NoRepeat = 0x4000;   // holding the keys down switches once, not over and over
    const int Probe = 9999;
    readonly List<int> ids = new List<int>();

    public event Action<int> Pressed;

    public HotkeyWindow() { CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); }   // message-only

    public bool Add(int id, string shortcut)
    {
        uint mods; Keys key;
        if (!Shortcut.TryParse(shortcut, out mods, out key)) return false;
        if (!RegisterHotKey(Handle, id, mods | NoRepeat, (uint)key)) return false;
        ids.Add(id);
        return true;
    }

    public void Clear()
    {
        foreach (int id in ids) UnregisterHotKey(Handle, id);
        ids.Clear();
    }

    // Is this combination free right now? Takes it for a moment and lets it go again. Another
    // program holding it is the only thing that makes this fail.
    public bool IsFree(string shortcut)
    {
        uint mods; Keys key;
        if (!Shortcut.TryParse(shortcut, out mods, out key)) return false;
        if (!RegisterHotKey(Handle, Probe, mods | NoRepeat, (uint)key)) return false;
        UnregisterHotKey(Handle, Probe);
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && Pressed != null) Pressed(m.WParam.ToInt32());   // WM_HOTKEY
        base.WndProc(ref m);
    }

    public void Dispose() { Clear(); DestroyHandle(); }
}

// A box that hands every key to the recorder - Tab, Enter, Esc, arrows, Backspace included - instead
// of the window using them to move about, close or edit.
class KeyBox : TextBox
{
    protected override bool IsInputKey(Keys keyData) { return true; }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) { return false; }
}

// The Shortcuts tab of the main window: click a box, press the keys; Clear empties one. Every change
// is saved at once; each line says whether its shortcut is ready, or what is wrong with it. While
// this tab is open the dock lets go of its shortcuts, so pressing one records it instead of switching.
class ShortcutsPage : UserControl
{
    class Line
    {
        public Func<string> Get, Default; public Action<string> Set; public Func<bool> IsOn;
        public TextBox Box; public Button Reset; public Label Status;
    }

    readonly List<Line> lines = new List<Line>();
    readonly ToolTip hints = new ToolTip();
    readonly Func<string, bool> isFree;   // null when the dock is not running to ask
    readonly Action save;

    public ShortcutsPage(Func<string, bool> isFree, Action save)
    {
        this.isFree = isFree;
        this.save = save;
        Font = new Font("Segoe UI", 9F);
        Dock = DockStyle.Fill;
        AutoScroll = true;      // a long list of categories scrolls rather than being cut off

        // name | keys | Clear | Reset | in next/previous | status. The Reset column keeps its width
        // while empty, so a Reset button appearing does not shift everything sideways.
        var table = new TableLayoutPanel { ColumnCount = 6, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(12, 12) };
        for (int i = 0; i < 6; i++)
            table.ColumnStyles.Add(i == 3 ? new ColumnStyle(SizeType.Absolute, 64) : new ColumnStyle(SizeType.AutoSize));
        int row = 0;

        // off until you turn it on: the suggested keys are filled in, but no key is taken away from
        // any other program before you say so
        var on = new CheckBox { Text = "Turn on keyboard shortcuts", AutoSize = true, Checked = Config.ShortcutsOn,
                                Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 0) };
        on.CheckedChanged += delegate { Config.ShortcutsOn = on.Checked; save(); UpdateLines(); };
        var top = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(3, 0, 3, 14) };
        top.Controls.Add(on);
        top.Controls.Add(new TabHelp("The Shortcuts tab",
            "# Keyboard shortcuts",
            "Anywhere: they work in every program while LinkPilot is in the dock.",
            "Suggested keys: filled in - tick Turn on. Change, untick or Reset each one.",
            "# Shortcut",
            "Change it: click the box and press any key or combination - media keys and F13-F24 too.",
            "A keyboard button shows nothing: give it F13-F24 in the keyboard's own software (Logi Options+).",
            "# In next / previous",
            "Ticked: Next and Previous step through these categories only.",
            "# Status",
            "Says: ready, taken by another program, used twice, or types a character.",
            "Types a character: allowed, but that character can't be typed while the shortcut is on - e.g. on a Polish keyboard Ctrl+Alt+A is AltGr+A, so typing ą would switch browsers instead.") { Margin = new Padding(6, 0, 0, 0) });
        table.Controls.Add(top, 0, row);
        table.SetColumnSpan(top, 6);
        row++;

        table.Controls.Add(Ui.Caption("Active"), 0, row);
        table.Controls.Add(Ui.Caption("Shortcut"), 1, row);
        var cycleCaption = Ui.Caption("In next / previous");
        cycleCaption.Margin = new Padding(8, 2, 8, 2);
        table.Controls.Add(cycleCaption, 4, row);
        table.Controls.Add(Ui.Caption("Status"), 5, row);
        row++;

        foreach (var c in Config.Categories)
        {
            var cat = c;
            AddLine(table, row++, cat.Name, () => cat.HotKey, v => cat.HotKey = v, () => cat.DefaultKey, cat,
                    () => cat.KeyOn, v => cat.KeyOn = v);
        }
        AddLine(table, row++, "Next category", () => Config.NextKey, v => Config.NextKey = v, () => Config.NextDefault, null,
                () => Config.NextOn, v => Config.NextOn = v);
        AddLine(table, row++, "Previous category", () => Config.PrevKey, v => Config.PrevKey = v, () => Config.PrevDefault, null,
                () => Config.PrevOn, v => Config.PrevOn = v);
        AddLine(table, row++, "Rules on / off", () => Config.RulesKey, v => Config.RulesKey = v, () => Config.RulesDefault, null,
                () => Config.RulesKeyOn, v => Config.RulesKeyOn = v);
        Controls.Add(table);
        UpdateLines();
    }

    // One line: a tick with the name (is this shortcut in use?), the keys, Clear, Reset (only once
    // changed from the suggested keys), and for a category, whether next / previous include it.
    void AddLine(TableLayoutPanel table, int row, string name, Func<string> get, Action<string> set,
                 Func<string> getDefault, Category cycleOf, Func<bool> isOn, Action<bool> setOn)
    {
        var line = new Line { Get = get, Set = set, Default = getDefault, IsOn = isOn };
        var active = new CheckBox { Text = name, AutoSize = true, Checked = isOn(), Anchor = AnchorStyles.Left,
                                    Margin = new Padding(3, 5, 14, 6) };
        active.CheckedChanged += delegate { setOn(active.Checked); save(); UpdateLines(); };
        hints.SetToolTip(active, "Ticked: this shortcut is in use. Unticked: its keys are kept, but pressing them does nothing.");
        line.Box = new KeyBox { Width = 190, ReadOnly = true, BackColor = SystemColors.Window, Cursor = Cursors.Hand };
        var clear = new Button { Text = "Clear", AutoSize = true, Margin = new Padding(6, 3, 3, 3) };
        clear.Click += delegate { line.Set(""); save(); UpdateLines(); };
        line.Reset = new Button { Text = "Reset", AutoSize = true, Margin = new Padding(6, 3, 3, 3), Visible = false };
        line.Reset.Click += delegate { line.Set(line.Default()); save(); UpdateLines(); };
        line.Status = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(10, 7, 3, 6) };
        line.Box.KeyDown += (s, e) => Record(line, e.KeyCode, e);
        // Print Screen only ever tells a window that it was let go, never that it was pressed
        line.Box.KeyUp += (s, e) => { if (e.KeyCode == Keys.PrintScreen) Record(line, Keys.PrintScreen, e); };
        table.Controls.Add(active, 0, row);
        table.Controls.Add(line.Box, 1, row);
        table.Controls.Add(clear, 2, row);
        table.Controls.Add(line.Reset, 3, row);
        if (cycleOf != null)
        {
            var cat = cycleOf;
            var cycle = new CheckBox { Checked = cat.InCycle, AutoSize = true, Anchor = AnchorStyles.None };
            cycle.CheckedChanged += delegate { cat.InCycle = cycle.Checked; save(); };
            hints.SetToolTip(cycle, "Ticked: Next and Previous stop at " + cat.Name + ". Its own shortcut works either way.");
            table.Controls.Add(cycle, 4, row);
        }
        table.Controls.Add(line.Status, 5, row);
        lines.Add(line);
    }

    // Any key is taken as it is - with whatever of Ctrl, Alt, Shift and Win is held down. Whether it
    // is a wise choice is yours; the line says what it would get in the way of.
    void Record(Line line, Keys k, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        if (k == Keys.ControlKey || k == Keys.Menu || k == Keys.ShiftKey || k == Keys.LWin || k == Keys.RWin ||
            k == Keys.None || k == Keys.ProcessKey || k == Keys.Packet) return;   // wait for the actual key
        line.Set(Shortcut.Format(e.Control, e.Alt, e.Shift, Shortcut.WinDown(), k));
        save();
        UpdateLines();
    }

    // Windows does not tell which program holds a shortcut - so the hover says what usually does.
    const string TakenHint =
        "Another program running now has claimed these keys, so they would not reach LinkPilot.\n" +
        "Windows does not say which program. Often: PowerToys, AutoHotkey, Discord, Steam,\n" +
        "screenshot tools, or the graphics driver's overlay (NVIDIA, AMD).\n" +
        "Keyboard and mouse software that only presses the keys for you (Logi Options+) does not claim them.";

    // Checked again each time the tab is opened: it may have been built in the background while the
    // dock still held its keys - which then looked "taken by another program".
    public void CheckAgain() { UpdateLines(); }

    void UpdateLines()
    {
        // only shortcuts in use can clash with each other
        var all = lines.Where(x => x.IsOn()).Select(x => x.Get()).Where(x => x.Length > 0).ToList();
        foreach (var line in lines)
        {
            string s = line.Get(), def = line.Default();
            line.Box.Text = s.Length > 0 ? s : "(none)";
            line.Box.ForeColor = line.IsOn() ? SystemColors.WindowText : SystemColors.GrayText;
            line.Reset.Visible = !string.Equals(s, def, StringComparison.OrdinalIgnoreCase);
            hints.SetToolTip(line.Reset, "Back to the suggested one: " + (def.Length > 0 ? def : "none"));
            if (s.Length == 0) { line.Status.Text = ""; continue; }
            if (!line.IsOn())
            {
                line.Status.Text = "off - kept, but not in use";
                line.Status.ForeColor = SystemColors.GrayText;
                continue;
            }
            string typed = Shortcut.TypesCharacter(s);
            if (typed == " ") typed = "a space";
            else if (typed != null) typed = "“" + typed + "”";
            string problem =
                all.Count(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase)) > 1 ? "used twice here" :
                typed != null ? "types " + typed :
                isFree != null && !isFree(s) ? "taken by another program" : null;
            line.Status.Text = problem != null ? "⚠ " + problem : Config.ShortcutsOn ? "✓ ready" : "ready - tick Turn on";
            hints.SetToolTip(line.Status, problem == "taken by another program" ? TakenHint : "");
            line.Status.ForeColor = problem != null ? Color.DarkOrange : Config.ShortcutsOn ? Color.SeaGreen : SystemColors.GrayText;
        }
    }
}
