// Small pieces shared by the windows: the round "?" that explains something when you point at it
// or click it, and headings that carry one. The explanations live there instead of in long texts
// across the windows.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

static class Ui
{
    public static readonly Color Accent = Color.FromArgb(0, 103, 192);
    public static readonly Color Bar = Color.FromArgb(240, 240, 240);        // the strip along a window's bottom
    public static readonly Color Picked = Color.FromArgb(204, 228, 247);     // a selected line

    // "Categories (?)" - bold text with its "?" after it
    public static FlowLayoutPanel Heading(string text, string help, float size = 9.75F)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(3, 0, 3, 4), BackColor = Color.Transparent };
        row.Controls.Add(new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", size, FontStyle.Bold),
                                     Margin = new Padding(0, 1, 0, 0) });
        if (help != null) row.Controls.Add(new HelpMark(help));
        return row;
    }

    // a column title in a table - small and grey - with its "?"
    public static FlowLayoutPanel Caption(string text, string help)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(3, 0, 3, 2) };
        row.Controls.Add(new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 8F), ForeColor = SystemColors.GrayText,
                                     Margin = new Padding(0, 2, 0, 0) });
        if (help != null) row.Controls.Add(new HelpMark(help) { Size = new Size(17, 17), Margin = new Padding(4, 0, 0, 0) });
        return row;
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
    public static Panel Section(string text, string help)
    {
        var p = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(2, 6, 0, 0) };
        p.Controls.Add(Heading(text, help));
        return p;
    }
}

// A round "?". Pointing at it shows its explanation; clicking it shows the same and keeps it up.
class HelpMark : Control
{
    static readonly ToolTip tip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 150, ReshowDelay = 100 };
    readonly string text;
    bool over;

    public HelpMark(string text)
    {
        this.text = Wrap(text, 70);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Size = new Size(20, 20);
        Margin = new Padding(5, 1, 0, 0);
        Cursor = Cursors.Help;
        TabStop = false;
        AccessibleRole = AccessibleRole.HelpBalloon;
        AccessibleName = "Help";
        AccessibleDescription = text;
        tip.SetToolTip(this, this.text);
    }

    protected override void OnMouseEnter(EventArgs e) { over = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { over = false; Invalidate(); tip.Hide(this); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { tip.Show(text, this, Width / 2, Height + 4, 30000); base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // a light blue disc with a blue ring and question mark; pointed at, it fills in blue
        var ring = new RectangleF(0.75F, 0.75F, Width - 2.5F, Height - 2.5F);
        using (var fill = new SolidBrush(over ? Ui.Accent : Color.FromArgb(232, 241, 252))) g.FillEllipse(fill, ring);
        using (var edge = new Pen(over ? Ui.Accent : Color.FromArgb(110, 160, 215), 1.25F)) g.DrawEllipse(edge, ring);
        using (var f = new Font("Segoe UI", Height * 0.6F, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var ink = new SolidBrush(over ? Color.White : Ui.Accent))
        using (var centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.DrawString("?", f, ink, new RectangleF(0, 0.5F, Width, Height - 0.5F), centre);
        }
    }

    // A Windows tooltip does not wrap long text by itself.
    static string Wrap(string s, int width)
    {
        var sb = new StringBuilder();
        foreach (string para in s.Split('\n'))
        {
            int line = 0;
            foreach (string word in para.Split(' '))
            {
                if (line > 0 && line + 1 + word.Length > width) { sb.Append("\r\n"); line = 0; }
                else if (line > 0) { sb.Append(' '); line++; }
                sb.Append(word);
                line += word.Length;
            }
            sb.Append("\r\n");
        }
        return sb.ToString().TrimEnd();
    }
}
