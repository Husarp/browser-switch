// Updates: the only time LinkPilot goes online.
//
// It asks GitHub one question - "what is the newest release of Husarp/linkpilot?" - and nothing
// is sent with it except what every web request carries. It asks when you press Check now, and once
// a day on its own only if you turn that on (off to begin with). Updating runs get.ps1, the same
// one-command installer: it downloads the new version's source code from GitHub and builds it here.

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

static class Updater
{
    public const string Repo = "Husarp/linkpilot";
    public static string Latest;   // the newest version GitHub reported since the program started, or null

    public static string Current
    {
        get { var v = typeof(Updater).Assembly.GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; }
    }

    public static bool UpdateWaiting { get { return Latest != null && IsNewer(Latest, Current); } }

    public static bool IsNewer(string a, string b)
    {
        Version va, vb;
        return Version.TryParse(a, out va) && Version.TryParse(b, out vb) && va > vb;
    }

    // Asks GitHub for the newest release's version number - "3.9.1". Throws if it cannot.
    public static string AskGitHub()
    {
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2, which GitHub requires
        using (var web = new WebClient())
        {
            web.Headers[HttpRequestHeader.UserAgent] = "BrowserSwitch/" + Current;
            web.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            string json = web.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
            var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9]+(\\.[0-9]+){1,3})\"");
            if (!m.Success) throw new InvalidDataException("GitHub's answer had no version in it.");
            return m.Groups[1].Value;
        }
    }

    // Asks on a background thread; done(newest, problem) runs back on ui's thread.
    public static void CheckInBackground(Control ui, Action<string, string> done)
    {
        new Thread(() =>
        {
            string latest = null, problem = null;
            try { latest = AskGitHub(); Latest = latest; Config.UpdateChecked = DateTime.Now; }
            catch (Exception e) { problem = e.Message; }
            try { ui.BeginInvoke((MethodInvoker)(() => done(latest, problem))); } catch { }
        }) { IsBackground = true }.Start();
    }

    // A development copy - a git clone - is updated with git, never overwritten from a release.
    public static bool IsDevelopmentCopy { get { return Directory.Exists(Path.Combine(Config.Dir, ".git")); } }

    // Runs get.ps1 - the copy that came with this version - in a window you can watch: it downloads
    // the newest release's source code, builds it, puts it here in place of this one (your settings
    // are kept) and starts it again. Returns what went wrong, or null.
    public static string StartUpdate()
    {
        if (IsDevelopmentCopy) return "This copy is a development folder (a git clone) - update it with git pull and build.cmd.";
        try { Process.Start(UpdateProcess()); return null; }
        catch (Exception e) { return e.Message; }
    }

    // The update, as the process that runs it - separate so the tests can check the command.
    public static ProcessStartInfo UpdateProcess()
    {
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        string script = Path.Combine(Config.Dir, "get.ps1");
        string run = File.Exists(script)
            ? "& '" + script.Replace("'", "''") + "'"
            : "irm https://raw.githubusercontent.com/" + Repo + "/main/get.ps1 | iex";
        var psi = new ProcessStartInfo(powershell, "-NoProfile -ExecutionPolicy Bypass -Command \"" + run +
                                       "; Write-Host ''; Write-Host 'This window closes in 10 seconds.'; Start-Sleep 10\"")
                  { UseShellExecute = false };
        psi.EnvironmentVariables["BROWSERSWITCH_DIR"] = Config.Dir;
        psi.EnvironmentVariables["BROWSERSWITCH_UPDATE"] = "1";
        return psi;
    }

    public static void OpenReleasePage()
    {
        try { Process.Start("https://github.com/" + Repo + "/releases/latest"); } catch { }
    }
}

// The About & updates tab: the version, what "offline" means here, and updates.
class AboutPage : UserControl
{
    readonly Action save;
    readonly Label status = new Label { AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
    readonly Button check, update;
    readonly LinkLabel whatsNew;

    public AboutPage(Action save, Action showSetup)
    {
        this.save = save;
        Font = new Font("Segoe UI", 9F);
        Dock = DockStyle.Fill;
        AutoScroll = true;

        var column = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(18, 14) };
        column.Controls.Add(new Label { Text = "LinkPilot " + Updater.Current, AutoSize = true, Font = new Font("Segoe UI", 14F, FontStyle.Bold) });
        column.Controls.Add(new Label { Text = "Decides which browser - and which profile - every link opens in.", AutoSize = true,
                                        ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 14) });

        // what offline means
        column.Controls.Add(new Label { Text = "Works offline", AutoSize = true, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold) });
        column.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(3, 2, 3, 14),
            Text = "Your links, categories, rules and the link log never leave this computer. The only time LinkPilot " +
                   "connects to the internet is to ask GitHub (github.com) whether a newer version exists - when you press " +
                   "Check now, or once a day if you allow it below - and, when you press Update now, to download the new " +
                   "version's source code, which is then built on this PC. Nothing about your links or settings is sent; " +
                   "GitHub sees only what any visit to a website shows, such as your internet address." });

        // updates
        column.Controls.Add(new Label { Text = "Updates", AutoSize = true, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold) });
        var auto = new CheckBox { Text = "Check for updates automatically, once a day", AutoSize = true, Checked = Config.UpdateCheck,
                                  Margin = new Padding(3, 6, 3, 3) };
        auto.CheckedChanged += delegate { Config.UpdateCheck = auto.Checked; save(); };
        column.Controls.Add(auto);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        check = new Button { Text = "Check now", AutoSize = true };
        check.Click += delegate { Check(); };
        update = new Button { Text = "Update now", AutoSize = true, Visible = false, Font = new Font(Font, FontStyle.Bold) };
        update.Click += delegate { RunUpdate(); };
        whatsNew = new LinkLabel { Text = "What's new", AutoSize = true, Visible = false, Margin = new Padding(10, 8, 3, 3) };
        whatsNew.LinkClicked += delegate { Updater.OpenReleasePage(); };
        row.Controls.AddRange(new Control[] { check, update, whatsNew });
        column.Controls.Add(row);
        column.Controls.Add(status);
        column.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 2, 3, 14),
            Text = "Updating does what the one-command installer does: downloads the newest release's source code from GitHub, " +
                   "builds it with the C# compiler that is part of Windows, and puts it in place of this copy. Your settings stay. " +
                   "You can also update by running the install command again." });

        // the window
        column.Controls.Add(new Label { Text = "Window", AutoSize = true, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold) });
        var taskbar = new CheckBox { Text = "Show a taskbar button while this window is open", AutoSize = true, Checked = Config.TaskbarButton,
                                     Margin = new Padding(3, 6, 3, 3) };
        taskbar.CheckedChanged += delegate
        {
            Config.TaskbarButton = taskbar.Checked;
            save();
            var form = FindForm();
            if (form != null) form.ShowInTaskbar = taskbar.Checked;
        };
        column.Controls.Add(taskbar);
        column.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 14),
            Text = "Either way LinkPilot keeps running next to the clock after the window is closed - click its icon there " +
                   "(under the ^ arrow, if Windows has hidden it) to open this window again." });

        // more
        column.Controls.Add(new Label { Text = "More", AutoSize = true, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold) });
        var setup = new LinkLabel { Text = "Show the setup screen again", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
        setup.LinkClicked += delegate { showSetup(); };
        var code = new LinkLabel { Text = "LinkPilot on GitHub - the code, releases, and where to report a problem", AutoSize = true };
        code.LinkClicked += delegate { try { Process.Start("https://github.com/" + Updater.Repo); } catch { } };
        column.Controls.Add(setup);
        column.Controls.Add(code);
        Controls.Add(column);
        ShowState(null);
    }

    void Check()
    {
        check.Enabled = false;
        status.ForeColor = SystemColors.GrayText;
        status.Text = "Asking GitHub...";
        Updater.CheckInBackground(this, (latest, problem) =>
        {
            check.Enabled = true;
            if (problem != null) { status.ForeColor = Color.DarkOrange; status.Text = "Could not reach GitHub: " + problem; return; }
            save();   // remembers when it last checked
            ShowState(latest);
        });
    }

    void ShowState(string latest)
    {
        bool waiting = Updater.UpdateWaiting;
        update.Visible = whatsNew.Visible = waiting;
        if (waiting)
        {
            update.Text = "Update now to " + Updater.Latest;
            status.ForeColor = Color.SeaGreen;
            status.Text = "LinkPilot " + Updater.Latest + " is available - you have " + Updater.Current + ".";
        }
        else if (latest != null)
        {
            status.ForeColor = Color.SeaGreen;
            status.Text = "✓ You have the newest version.";
        }
        else
        {
            status.ForeColor = SystemColors.GrayText;
            status.Text = Config.UpdateChecked == DateTime.MinValue ? "Not checked yet." : "Last checked " + Config.UpdateChecked.ToString("yyyy-MM-dd HH:mm") + ".";
        }
    }

    void RunUpdate()
    {
        if (MessageBox.Show(FindForm(), "Update LinkPilot to " + Updater.Latest + "?\n\nA window shows what it does: it downloads the new version's " +
                            "source code from GitHub, builds it on this PC and starts it again. Your settings stay.",
                            "LinkPilot", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        string problem = Updater.StartUpdate();
        if (problem != null) MessageBox.Show(FindForm(), problem, "LinkPilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
