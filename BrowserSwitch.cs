// Browser Switch - a stand-in browser that forwards links to whichever real browser is "on".
//
// Windows will not let a script change your default browser: the setting is signed with a hash tied
// to your account, so anything that writes it directly gets thrown away. That protection exists to
// stop programs hijacking your browser, and it is a good thing. So instead of fighting it, this
// program IS the default browser. It takes the link and hands it to Firefox or to Chrome depending on
// one word in mode.txt. Flipping that word is the one click.
//
// Nothing here is downloaded: built with the C# compiler that ships inside Windows.
//
//   BrowserSwitch.exe <url>        open the link in the browser that is currently on
//   BrowserSwitch.exe --toggle     flip personal <-> work, with a small message on screen
//   BrowserSwitch.exe --set work   set it without asking (used by the repair script)
//   BrowserSwitch.exe --dry <url>  work out where the link WOULD go, write it to dry-run.log, open
//                                  nothing (this is how the switcher is tested)
//
// If anything at all goes wrong, the link still opens: every failure falls back to the personal
// browser, and then to whatever browser can be found. A broken switcher must never mean a dead link.

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

static class BrowserSwitch
{
    const string Personal = "personal";
    const string Work = "work";

    static string Dir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
    static string Near(string name) { return Path.Combine(Dir, name); }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) { Open(null); return 0; }

            switch (args[0])
            {
                case "--toggle":
                    string now = Flip();
                    Toast(now == Work ? "WORK" : "PERSONAL", Path.GetFileNameWithoutExtension(BrowserFor(now)),
                          now == Work ? Color.FromArgb(198, 93, 42) : Color.FromArgb(52, 96, 168));
                    return 0;

                case "--set":
                    if (args.Length > 1) Write(Near("mode.txt"), Normalise(args[1]));
                    return 0;

                case "--dry":
                    string target = BrowserFor(Mode());
                    Write(Near("dry-run.log"), Mode() + "\t" + target + "\t" + (args.Length > 1 ? args[1] : ""));
                    return 0;

                default:
                    Open(args[0]);
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Note(ex.ToString());
            try { Start(AnyBrowser(), args.Length > 0 ? args[0] : null); } catch { }
            return 1;
        }
    }

    // ---- what is on right now -------------------------------------------------------------------

    static string Mode()
    {
        try
        {
            string m = File.ReadAllText(Near("mode.txt")).Trim();
            return Normalise(m);
        }
        catch { return Personal; }          // no file, unreadable, anything: behave exactly as before
    }

    static string Normalise(string m)
    {
        return string.Equals(m, Work, StringComparison.OrdinalIgnoreCase) ? Work : Personal;
    }

    static string Flip()
    {
        string next = Mode() == Work ? Personal : Work;
        Write(Near("mode.txt"), next);
        return next;
    }

    // ---- which browser that means ---------------------------------------------------------------

    // browsers.txt holds two lines, so the pair can be changed without rebuilding anything:
    //   personal=C:\Program Files\Mozilla Firefox\firefox.exe
    //   work=C:\Program Files\Google\Chrome\Application\chrome.exe
    static string BrowserFor(string mode)
    {
        try
        {
            foreach (string line in File.ReadAllLines(Near("browsers.txt")))
            {
                string s = line.Trim();
                if (s.Length == 0 || s.StartsWith("#")) continue;
                int eq = s.IndexOf('=');
                if (eq < 1) continue;
                if (string.Equals(s.Substring(0, eq).Trim(), mode, StringComparison.OrdinalIgnoreCase))
                {
                    string exe = s.Substring(eq + 1).Trim().Trim('"');
                    if (File.Exists(exe)) return exe;
                }
            }
        }
        catch { }
        return AnyBrowser();
    }

    // last resort, in the order someone would want them
    static string AnyBrowser()
    {
        string[] guesses = {
            @"C:\Program Files\Mozilla Firefox\firefox.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        };
        foreach (string g in guesses) if (File.Exists(g)) return g;
        return "";
    }

    static void Open(string url)
    {
        string exe = BrowserFor(Mode());
        if (exe.Length == 0) throw new FileNotFoundException("no browser found");
        Start(exe, url);
    }

    static void Start(string exe, string url)
    {
        if (exe.Length == 0) return;
        var psi = new ProcessStartInfo(exe);
        if (!string.IsNullOrEmpty(url)) psi.Arguments = "\"" + url.Replace("\"", "") + "\"";
        psi.UseShellExecute = false;
        Process.Start(psi);
    }

    // ---- small things ---------------------------------------------------------------------------

    static void Write(string path, string text)
    {
        File.WriteAllText(path, text);
    }

    static void Note(string text)
    {
        try { File.AppendAllText(Near("errors.log"), DateTime.Now + "\r\n" + text + "\r\n\r\n"); } catch { }
    }

    // A label near the clock that says what just happened and takes itself away after a moment, so
    // switching stays one click - no OK button to press.
    static void Toast(string title, string subtitle, Color colour)
    {
        var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            TopMost = true,
            BackColor = colour,
            Size = new Size(240, 92),
        };
        Rectangle screen = Screen.PrimaryScreen.WorkingArea;
        form.Location = new Point(screen.Right - 260, screen.Bottom - 112);

        form.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = title + "\n" + subtitle,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
        });

        var timer = new Timer { Interval = 1300 };
        timer.Tick += delegate { timer.Stop(); form.Close(); };
        timer.Start();
        Application.Run(form);
    }
}
