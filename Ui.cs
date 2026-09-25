// Small pieces shared by the windows: the one (i) per tab that explains everything on it, and
// headings that can carry it. The explanations live there instead of in long texts across the windows.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Windows.Forms;

static class Ui
{
    public static readonly Color Accent = Color.FromArgb(0, 103, 192);
    public static readonly Color Bar = Color.FromArgb(240, 240, 240);        // the strip along a window's bottom
    public static readonly Color Picked = Color.FromArgb(204, 228, 247);     // a selected line

    // "Categories (i)" - bold text, with the tab's (i) after it if it has one
    public static FlowLayoutPanel Heading(string text, TabHelp help, float size = 9.75F)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(3, 0, 3, 4), BackColor = Color.Transparent };
        row.Controls.Add(new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", size, FontStyle.Bold),
                                     Margin = new Padding(0, 1, 0, 0) });
        if (help != null) row.Controls.Add(help);
        return row;
    }

    // a column title in a table - small and grey
    public static Label Caption(string text)
    {
        return new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 8F), ForeColor = SystemColors.GrayText,
                           Margin = new Padding(3, 2, 3, 2) };
    }

    // Everything you can click shows the hand cursor: every button and tick box in this window, and
    // any added to it later - tabs are built when opened, the switch row after every change.
    public static void HandCursors(Control c)
    {
        if (c is ButtonBase) c.Cursor = Cursors.Hand;   // buttons, tick boxes, choices
        c.ControlAdded += (s, e) => HandCursors(e.Control);
        foreach (Control child in c.Controls) HandCursors(child);
    }

    // A heading docked across the top of a panel.
    public static Panel Section(string text, TabHelp help = null)
    {
        var p = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(2, 6, 0, 0) };
        p.Controls.Add(Heading(text, help));
        return p;
    }
}

// The tab's (i) - drawn like the Android app's: a blue ring with an "i". Clicking it opens a panel
// under it that explains every section of the tab: each section's name in bold, then short points,
// each starting with a bold word ("Which wins: ..."). A click on the (i) again, anywhere else, or Esc, closes it.
//
//     new TabHelp("The Rules tab", "# Rules", "Which wins: ...", "No match: ...", "# App rules", ...)
//
// Lines starting with "# " are section names; the others are points.
class TabHelp : Control
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool HideCaret(IntPtr hwnd);
    static readonly Color Back = Color.FromArgb(236, 243, 254);
    readonly string title;
    readonly string[] lines;
    bool over;
    bool closedByClick;   // the panel was just closed by a click on this (i)

    public TabHelp(string title, params string[] lines)
    {
        this.title = title;
        this.lines = lines;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Size = new Size(22, 22);
        Margin = new Padding(6, 0, 0, 0);
        Cursor = Cursors.Hand;
        TabStop = false;
        AccessibleRole = AccessibleRole.HelpBalloon;
        AccessibleName = "How this works";
        AccessibleDescription = string.Join(" ", lines.Select(l => l.TrimStart('#', ' ')));
    }

    protected override void OnMouseEnter(EventArgs e) { over = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { over = false; closedByClick = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e)
    {
        if (closedByClick) closedByClick = false;   // this click just closed the panel: it stays closed
        else Open();
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var ring = new RectangleF(1.5F, 1.5F, Width - 4F, Height - 4F);
        Color ink = over ? Color.White : Ui.Accent;
        if (over) using (var fill = new SolidBrush(Ui.Accent)) g.FillEllipse(fill, ring);
        using (var edge = new Pen(Ui.Accent, 1.7F)) g.DrawEllipse(edge, ring);
        float cx = (Width - 1) / 2F;
        using (var dot = new SolidBrush(ink)) g.FillEllipse(dot, cx - 1.4F, Height * 0.26F, 2.8F, 2.8F);
        using (var bar = new Pen(ink, 2.2F) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(bar, cx, Height * 0.45F, cx, Height * 0.71F);
    }

    void Open()
    {
        var panel = BuildPanel();
        var host = new ToolStripControlHost(panel) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false };
        var drop = new ToolStripDropDown { Padding = Padding.Empty, DropShadowEnabled = true, AutoClose = true };
        drop.Items.Add(host);
        Fill(panel);   // only now: the drop-down hands its own font down to what it holds
        host.Size = panel.Size;
        // a click on this (i) while the panel is open closes it, as any click outside does - and the
        // same click, reaching the (i) next, must not open it again at once
        drop.Closing += (s, e) =>
        {
            closedByClick = e.CloseReason == ToolStripDropDownCloseReason.AppClicked && RectangleToScreen(ClientRectangle).Contains(Cursor.Position);
        };
        drop.Closed += delegate { BeginInvoke((MethodInvoker)drop.Dispose); };
        drop.Show(this, new Point(0, Height + 4));   // moved to stay on screen if needed
    }

    // The panel: the text laid out by a read-only rich text box, which does the wrapping and the bold.
    // The box has a font of its own, so no font handed down from outside can reach it - one would
    // reset all its formatting, bold and colours included, and leave its height measured for the old
    // text. (Separate from Open so a test can draw it without showing it.)
    internal Panel BuildPanel()
    {
        var box = new RichTextBox { BorderStyle = BorderStyle.None, ReadOnly = true, BackColor = Back, Width = 470, Height = 40,
                                    ScrollBars = RichTextBoxScrollBars.None, DetectUrls = false, TabStop = false,
                                    Cursor = Cursors.Arrow, Location = new Point(18, 14), Font = new Font("Segoe UI", 9.5F) };
        box.ContentsResized += (s, e) => box.Height = e.NewRectangle.Height + 4;
        box.GotFocus += delegate { HideCaret(box.Handle); };
        var panel = new Panel { BackColor = Back };
        panel.Controls.Add(box);
        panel.CreateControl();
        box.CreateControl();
        Fill(panel);
        return panel;
    }

    // The text, and the panel sized to it.
    internal void Fill(Panel panel)
    {
        var box = panel.Controls.OfType<RichTextBox>().First();
        box.ScrollBars = RichTextBoxScrollBars.None;
        box.Rtf = Rtf();
        int most = Screen.FromControl(this).WorkingArea.Height * 3 / 4;
        if (box.Height > most) { box.Height = most; box.ScrollBars = RichTextBoxScrollBars.Vertical; }
        panel.Size = new Size(box.Width + 36, box.Height + 28);
    }

    string Rtf()
    {
        var sb = new StringBuilder(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}{\colortbl ;\red0\green83\blue166;\red32\green33\blue36;}");
        sb.Append(@"\f0\cf2\fs28\b ").Append(Esc(title)).Append(@"\b0\par");
        foreach (string line in lines)
        {
            if (line.StartsWith("# "))
            {
                sb.Append(@"\pard\sb220\sa40\cf1\fs21\b ").Append(Esc(line.Substring(2))).Append(@"\b0\cf2\par");
                continue;
            }
            int colon = line.IndexOf(": ");
            sb.Append(@"\pard\fi-260\li260\tx260\sb70\fs19 \bullet\tab ");
            if (colon > 0 && colon <= 40)
                sb.Append(@"\b ").Append(Esc(line.Substring(0, colon + 1))).Append(@"\b0  ").Append(Esc(line.Substring(colon + 2)));   // one space ends \b0, the other is the text's
            else sb.Append(Esc(line));
            sb.Append(@"\par");
        }
        return sb.Append("}").ToString();
    }

    static string Esc(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (c == '\\' || c == '{' || c == '}') sb.Append('\\').Append(c);
            else if (c > 127) sb.Append(@"\u").Append((int)(short)c).Append('?');
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
