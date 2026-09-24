// The link log: every link Browser Switch handed on - when, from which app, where it opened and why,
// and whether it was changed on the way (a redirect skipped, tracking removed).
//
// It is kept only on this computer, in link-log.txt next to the program, one line per link, the
// newest 1000. Nothing is sent anywhere. It can be switched off, and cleared, on its tab.
//
// Each link is handed on by its own short-lived copy of the program, so two links clicked at once
// write at once: a named lock lets one finish before the other writes.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class LinkLog
{
    public const int Keep = 1000;
    const string LockName = @"Local\BrowserSwitch.LinkLog";

    public class Entry
    {
        public DateTime When;
        public string From = "", OpenedIn = "", Why = "", Asked = "", Opened = "", Changes = "";
    }

    public static string File_ { get { return Path.Combine(Config.Dir, "link-log.txt"); } }

    static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

    // One line per link: time, app, where it opened, why, the link as it came, the link as opened
    // (empty if unchanged), and what was changed.
    public static void Add(Entry e)
    {
        if (!Config.LogOn) return;
        try
        {
            using (var gate = new Mutex(false, LockName))
            {
                bool mine = false;
                try { mine = gate.WaitOne(3000); } catch (AbandonedMutexException) { mine = true; }
                try
                {
                    string line = string.Join("\t", e.When.ToString("yyyy-MM-dd HH:mm:ss"), Clean(e.From), Clean(e.OpenedIn), Clean(e.Why),
                                              Clean(e.Asked), e.Opened == e.Asked ? "" : Clean(e.Opened), Clean(e.Changes));
                    File.AppendAllText(File_, line + "\r\n", Encoding.UTF8);
                    // keep only the newest ones; checked by size, so most links cost nothing extra
                    if (new FileInfo(File_).Length > 600 * 1024)
                    {
                        var lines = File.ReadAllLines(File_, Encoding.UTF8);
                        File.WriteAllLines(File_, lines.Skip(Math.Max(0, lines.Length - Keep)).ToArray(), Encoding.UTF8);
                    }
                }
                finally { if (mine) gate.ReleaseMutex(); }
            }
        }
        catch (Exception ex) { Program.Note("link log: " + ex.Message); }
    }

    // Newest first.
    public static List<Entry> Read()
    {
        var list = new List<Entry>();
        try
        {
            if (!File.Exists(File_)) return list;
            foreach (string line in File.ReadAllLines(File_, Encoding.UTF8).Reverse().Take(Keep))
            {
                var b = line.Split('\t');
                if (b.Length < 5) continue;
                DateTime when;
                DateTime.TryParse(b[0], out when);
                list.Add(new Entry { When = when, From = b[1], OpenedIn = b[2], Why = b[3], Asked = b[4],
                                     Opened = b.Length > 5 && b[5].Length > 0 ? b[5] : b[4], Changes = b.Length > 6 ? b[6] : "" });
            }
        }
        catch { }
        return list;
    }

    public static void Clear() { try { File.Delete(File_); } catch { } }
}

// The Link log tab: the list, newest first; the chosen link in full underneath; copy it, open it
// again, clear the log, or switch logging off.
class LogPage : UserControl
{
    readonly Action save;
    readonly ListView list = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, MultiSelect = false,
                                            HeaderStyle = ColumnHeaderStyle.Nonclickable, HideSelection = false };
    readonly TextBox details = new TextBox { Dock = DockStyle.Bottom, Height = 78, Multiline = true, ReadOnly = true,
                                             BorderStyle = BorderStyle.FixedSingle, BackColor = SystemColors.Window, ScrollBars = ScrollBars.Vertical };
    readonly Label count = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(12, 8, 0, 0) };
    readonly Button copy, again;

    public LogPage(Action save)
    {
        this.save = save;
        Font = new Font("Segoe UI", 9F);
        Dock = DockStyle.Fill;
        Padding = new Padding(8, 6, 8, 6);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        var on = new CheckBox { Text = "Keep a log of links", AutoSize = true, Checked = Config.LogOn, Font = new Font(Font, FontStyle.Bold),
                                Margin = new Padding(3, 5, 3, 3) };
        on.CheckedChanged += delegate { Config.LogOn = on.Checked; save(); };
        top.Controls.Add(on);
        top.Controls.Add(new HelpMark("Every link Browser Switch hands on: when, which app it came from, where it opened and " +
            "why, and whether it was changed on the way - with the link as it came, and as it was opened.\n" +
            "The log stays on this computer only, in link-log.txt next to Browser Switch, and keeps the newest " +
            LinkLog.Keep + " links. Nothing is sent anywhere. Untick to stop logging; Clear log deletes it.")
            { Margin = new Padding(4, 8, 0, 0) });
        top.Controls.Add(count);

        list.Columns.Add("When", 118);
        list.Columns.Add("From", 100);
        list.Columns.Add("Opened in", 120);
        list.Columns.Add("Changed", 70);
        list.Columns.Add("Link", 300);
        list.Resize += delegate { FitLastColumn(); };
        list.SelectedIndexChanged += delegate { ShowSelected(); };
        list.DoubleClick += delegate { CopyLink(); };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(0, 6, 0, 0) };
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        refresh.Click += delegate { Fill(); };
        copy = new Button { Text = "Copy link", AutoSize = true, Enabled = false };
        copy.Click += delegate { CopyLink(); };
        again = new Button { Text = "Open again", AutoSize = true, Enabled = false };
        again.Click += delegate { OpenAgain(); };
        var clear = new Button { Text = "Clear log", AutoSize = true, Margin = new Padding(24, 3, 3, 3) };
        clear.Click += delegate
        {
            if (MessageBox.Show(FindForm(), "Delete the whole link log?", "Browser Switch", MessageBoxButtons.YesNo,
                                MessageBoxIcon.Question) != DialogResult.Yes) return;
            LinkLog.Clear();
            Fill();
        };
        buttons.Controls.AddRange(new Control[] { refresh, copy, again, clear });

        Controls.Add(list);
        Controls.Add(top);
        Controls.Add(details);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 6 });
        Controls.Add(buttons);
        Fill();

        // a new link is written by the short-lived copy of Browser Switch that handed it on; the list
        // follows, a moment later, keeping the line you had selected
        var watch = new FileSystemWatcher(Config.Dir, Path.GetFileName(LinkLog.File_)) { SynchronizingObject = this, EnableRaisingEvents = true };
        var settle = new System.Windows.Forms.Timer { Interval = 300 };
        FileSystemEventHandler changed = (s, e) => { settle.Stop(); settle.Start(); };
        watch.Changed += changed;
        watch.Created += changed;
        watch.Deleted += changed;
        settle.Tick += delegate { settle.Stop(); Fill(); };
        Disposed += delegate { watch.Dispose(); settle.Dispose(); };
    }

    void FitLastColumn()
    {
        int others = 0;
        for (int i = 0; i < list.Columns.Count - 1; i++) others += list.Columns[i].Width;
        list.Columns[list.Columns.Count - 1].Width = Math.Max(120, list.ClientSize.Width - others - 1);
    }

    void Fill()
    {
        var keep = Selected();
        var entries = LinkLog.Read();
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var e in entries)
        {
            var item = new ListViewItem(e.When == DateTime.MinValue ? "" : e.When.ToString("yyyy-MM-dd HH:mm")) { Tag = e };
            item.SubItems.Add(Friendly(e.From));
            item.SubItems.Add(e.OpenedIn);
            item.SubItems.Add(e.Changes.Length > 0 ? "yes" : "");
            item.SubItems.Add(e.Opened);
            list.Items.Add(item);
        }
        list.EndUpdate();
        count.Text = entries.Count == 0 ? (Config.LogOn ? "no links yet" : "") : entries.Count + (entries.Count == 1 ? " link" : " links");
        details.Text = entries.Count == 0 ? "Links you open from other programs will appear here." : "Select a link to see it in full.";
        if (keep != null)
            foreach (ListViewItem item in list.Items)
            {
                var e = (LinkLog.Entry)item.Tag;
                if (e.When == keep.When && e.Opened == keep.Opened) { item.Selected = true; item.EnsureVisible(); break; }
            }
        ShowSelected();
    }

    // "Signal.exe" as "Signal", using the app list's name where it knows the program
    static string Friendly(string exe)
    {
        if (string.IsNullOrEmpty(exe)) return "(unknown)";
        var app = AppCatalog.ForExe(exe);
        return app != null ? app.Name : Path.GetFileNameWithoutExtension(exe);
    }

    LinkLog.Entry Selected() { return list.SelectedItems.Count > 0 ? (LinkLog.Entry)list.SelectedItems[0].Tag : null; }

    void ShowSelected()
    {
        var e = Selected();
        copy.Enabled = again.Enabled = e != null;
        if (e == null) return;
        var sb = new StringBuilder();
        sb.Append("Opened:   " + e.Opened + "\r\n");
        if (e.Changes.Length > 0) sb.Append("It came as:   " + e.Asked + "\r\nChanged:   " + e.Changes + "\r\n");
        sb.Append("From " + Friendly(e.From) + " (" + (e.From.Length > 0 ? e.From : "unknown") + ") - went to " + e.Why);
        details.Text = sb.ToString();
    }

    void CopyLink()
    {
        var e = Selected(); if (e == null) return;
        try { Clipboard.SetText(e.Opened); } catch { }
    }

    // Hands the link on again exactly as a click would - through the rules and the live category.
    void OpenAgain()
    {
        var e = Selected(); if (e == null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath, "\"" + e.Opened.Replace("\"", "") + "\"") { UseShellExecute = false }); }
        catch { }
    }
}
