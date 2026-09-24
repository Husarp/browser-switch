// The icon .htm and .html files show in File Explorer and on the desktop.
//
// Windows takes it from Browser Switch's own registration (HKCU\Software\Classes\BrowserSwitchURL\
// DefaultIcon), so by default every web page file wore the Browser Switch arrows. Instead it follows
// the browser such a file would open in - a matching rule's, or the live category's - after every
// switch. Three looks, chosen in the dock's right-click menu:
//
//   page     a white page with that browser's icon on its corner (the default)
//   browser  that browser's icon, exactly as the dock shows it
//   own      Browser Switch's own icon, as before
//
// Windows only takes icons from files, so the icon is drawn once into file-icons\ next to the exe,
// under a name that changes whenever the picture does - Windows keeps icons it has seen by file
// name, and would go on showing the old one otherwise.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

static class FileIcon
{
    const string Key = @"Software\Classes\BrowserSwitchURL\DefaultIcon";
    static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
    static string last;   // what was set last time, so an unchanged switch costs nothing

    [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, int count, int flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

    public static string Describe(string style)
    {
        return style == "browser" ? "The browser's icon" : style == "own" ? "Browser Switch's own icon" : "A web page with the browser's icon";
    }

    // Brings the file icon up to date. Called by the dock after every change, and by --switch and
    // --reset. Does nothing until Browser Switch is installed (no registration to change).
    public static void Update()
    {
        try
        {
            using (var reg = Registry.CurrentUser.OpenSubKey(Key, true))
            {
                if (reg == null) return;
                string want = Wanted();
                if (want == null || want == last) return;
                if (!string.Equals(reg.GetValue("") as string, want, StringComparison.OrdinalIgnoreCase))
                {
                    reg.SetValue("", want);
                    SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);   // SHCNE_ASSOCCHANGED: redraw file icons
                }
                last = want;
                Tidy(want);
            }
        }
        catch (Exception e) { Program.Note("file icon: " + e.Message); }
    }

    // The DefaultIcon value for now: the exe itself, or an icon file drawn for the browser a
    // double-clicked web page file would open in.
    static string Wanted()
    {
        string own = Path.Combine(Config.Dir, "BrowserSwitch.exe") + ",0";
        if (Config.FileIconStyle == "own") return own;
        string exe, extra, why;
        Category c;
        Program.Resolve("file:///C:/page.htm", "explorer.exe", out exe, out extra, out why, out c);
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return own;

        // the name says what the picture shows: the style, the browser and profile, the chosen look,
        // and when the profile's own icon file last changed (a new profile picture)
        string profileIcon = Machine.ProfileIcon(exe, extra);
        string said = Config.FileIconStyle + "|" + exe + "|" + extra + "|" + (c != null ? c.Icon : "") + "|" +
                      (profileIcon != null && File.Exists(profileIcon) ? File.GetLastWriteTimeUtc(profileIcon).Ticks.ToString() : "");
        string name;
        using (var md5 = MD5.Create())
            name = Config.FileIconStyle + "-" + BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(said))).Replace("-", "").Substring(0, 12) + ".ico";
        string dir = Path.Combine(Config.Dir, "file-icons");
        string path = Path.Combine(dir, name);
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(dir);
            using (var big = Picture(c, exe, extra)) Save(big, path, Config.FileIconStyle == "page");
        }
        return path;
    }

    // Icons from earlier switches are no longer needed.
    static void Tidy(string keep)
    {
        try
        {
            string dir = Path.Combine(Config.Dir, "file-icons");
            if (!Directory.Exists(dir)) return;
            foreach (string f in Directory.GetFiles(dir, "*.ico"))
                if (!string.Equals(f, keep, StringComparison.OrdinalIgnoreCase)) try { File.Delete(f); } catch { }
        }
        catch { }
    }

    // ---- the picture ----------------------------------------------------------------------------

    // The category's dock icon, large: the same choices as Tray.IconFor, drawn at 256 pixels
    // instead of 32 so it stays sharp on a desktop with big icons.
    static Bitmap Picture(Category c, string exe, string args)
    {
        string spec = c != null ? c.Icon ?? "" : "";
        try
        {
            if (spec.StartsWith("color:", StringComparison.OrdinalIgnoreCase))
                return Letter(c.Name, ColorTranslator.FromHtml(spec.Substring(6)));
            if (spec.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                string file = spec.Substring(5);
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".ico" || ext == ".exe") return Extract(file);
                using (var img = Image.FromFile(file)) return Fit(img);
            }
            var own = Extract(Machine.ProfileIcon(exe, args) ?? exe);
            if (spec.StartsWith("tint:", StringComparison.OrdinalIgnoreCase))
            {
                int[] v = spec.Substring(5).Split(',').Select(x => int.Parse(x.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                using (own) return Tray.Recolour(own, v[0], v[1], v[2]);
            }
            return own;
        }
        catch { return Letter(c != null ? c.Name : Path.GetFileNameWithoutExtension(exe), Color.Gray); }
    }

    // The largest picture in an exe or .ico file, up to 256 pixels.
    static Bitmap Extract(string file)
    {
        var handles = new IntPtr[1];
        var ids = new int[1];
        if (PrivateExtractIcons(file, 0, 256, 256, handles, ids, 1, 0) < 1 || handles[0] == IntPtr.Zero)
            throw new IOException("no icon in " + file);
        try { using (var icon = Icon.FromHandle(handles[0])) return icon.ToBitmap(); }
        finally { DestroyIcon(handles[0]); }
    }

    static Bitmap Fit(Image img)
    {
        var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float scale = Math.Min(256f / img.Width, 256f / img.Height);
            float w = img.Width * scale, h = img.Height * scale;
            g.DrawImage(img, (256 - w) / 2, (256 - h) / 2, w, h);
        }
        return bmp;
    }

    static Bitmap Letter(string name, Color color)
    {
        var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        using (var fill = new SolidBrush(color))
        using (var font = new Font("Segoe UI", 150F, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var ink = new SolidBrush((color.R * 299 + color.G * 587 + color.B * 114) / 1000 > 160 ? Color.Black : Color.White))
        using (var centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.FillEllipse(fill, 8, 8, 240, 240);
            g.DrawString(name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?", font, ink, new RectangleF(0, 8, 256, 256), centre);
        }
        return bmp;
    }

    // One size of the icon: the browser's picture alone, or on a page.
    static Bitmap Draw(Bitmap browser, int size, bool page)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (!page) { g.DrawImage(browser, 0, 0, size, size); return bmp; }

            // a white page with a folded corner and a few grey lines of "text"; the browser's icon
            // over its lower right, as Windows draws web page files for other browsers
            float s = size / 100f;
            float left = 14 * s, top = 2 * s, right = 80 * s, bottom = 96 * s, fold = 18 * s;
            using (var sheet = new GraphicsPath())
            {
                sheet.AddLine(left, top, right - fold, top);
                sheet.AddLine(right - fold, top, right, top + fold);
                sheet.AddLine(right, top + fold, right, bottom);
                sheet.AddLine(right, bottom, left, bottom);
                sheet.CloseFigure();
                using (var fill = new SolidBrush(Color.FromArgb(250, 250, 250))) g.FillPath(fill, sheet);
                using (var edge = new Pen(Color.FromArgb(150, 150, 156), Math.Max(1f, 1.5f * s))) g.DrawPath(edge, sheet);
            }
            using (var corner = new GraphicsPath())
            {
                corner.AddLine(right - fold, top, right - fold, top + fold);
                corner.AddLine(right - fold, top + fold, right, top + fold);
                corner.CloseFigure();
                using (var fill = new SolidBrush(Color.FromArgb(215, 215, 220))) g.FillPath(fill, corner);
            }
            if (size >= 32)   // too small to read below that - just clutter
                using (var line = new SolidBrush(Color.FromArgb(205, 208, 215)))
                    for (int i = 0; i < 4; i++)
                        g.FillRectangle(line, left + 9 * s, top + (26 + i * 10) * s, (i == 3 ? 26 : 44) * s, 4 * s);
            g.DrawImage(browser, 42 * s, 42 * s, 56 * s, 56 * s);
        }
        return bmp;
    }

    // An .ico holding every size, each stored as PNG (Windows Vista and later read that).
    static void Save(Bitmap browser, string path, bool page)
    {
        var frames = new List<byte[]>();
        foreach (int size in Sizes)
            using (var bmp = Draw(browser, size, page))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                frames.Add(ms.ToArray());
            }
        using (var f = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(f))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)Sizes.Length);
            int offset = 6 + 16 * Sizes.Length;
            for (int i = 0; i < Sizes.Length; i++)
            {
                w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
                w.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(frames[i].Length); w.Write(offset);
                offset += frames[i].Length;
            }
            foreach (var frame in frames) w.Write(frame);
        }
    }
}
