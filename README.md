# Browser Switch

Decides which browser — and which **profile** of it — every link on this computer opens in. You make
named categories like Work, Home or School, point each at a browser profile, and switch between them
with one click.

## Why it is built this way

Windows will not let a script change your default browser. The setting carries a signature tied to
your account, and anything written directly is thrown away. That protection stops programs hijacking
your browser, and it is worth keeping.

So this does not fight it. **Browser Switch becomes the default browser** and passes each link
straight on to the real one, based on which category is live.

Nothing was downloaded to build it. `BrowserSwitch.exe` is about 110 KB, much of it the icon,
compiled from the four `.cs` files and `BrowserSwitch.ico` by the C# compiler already inside Windows
(`build.cmd` rebuilds it).

## The dock

Browser Switch lives in the **notification area** next to the clock — "the dock" — and starts there
when you sign in. It has no taskbar button.

- **Click its icon** (blue square, two arrows) — the window opens. Closing the window puts it away
  again; the icon stays.
- **Right-click it** — your categories, with a tick on the live one: click one to switch. Also
  *Open Browser Switch* and *Exit*.
- **Pin a category** — in the window, select it and tick **Show in dock**. It gets its own icon; one
  click on that icon switches to it, with no window. **Dock icon…** chooses what it looks like: the
  browser's own icon — for Brave, Chrome and Edge the one **with the profile's picture on it**, the
  same the taskbar shows, which the browser keeps in the profile's folder; that icon **recoloured** — a small editor with *Colour*, *Strength* and
  *Brightness* sliders and a live preview, for two profiles of the same browser (a green Brave for
  Work, an orange one for Home); a colour with the category's first letter; or an image file.
- **Hover over a pinned icon** and it says which category it is: **Work**, or **Switch to Work** —
  choose which in the right-click menu, *Hover text on dock icons*.
- **Every switch shows a short note** at the bottom of the screen for about two seconds —
  "Links now open in: Work", and under it the browser and profile with the category's dock icon
  in front (recoloured, if you recoloured it). It never takes the focus and clicks go straight
  through it. It appears whichever way you switched: an icon, a shortcut, the window, or
  `Back to normal.cmd`.

Windows puts new icons in the hidden-icons area first — the **^** arrow next to the clock. To keep
one always visible, drag it from there onto the taskbar, or switch it on in Settings →
Personalization → Taskbar → *Other system tray icons*.

## Keyboard shortcuts

**Off until you turn them on:** in the window, **Shortcuts…** (end of the *Switch to* row), then tick
**Turn on keyboard shortcuts**. They then work anywhere, as long as the dock is running.

- **One per category**, and **Next** / **Previous**, which step through every category that has a
  browser, in order, round and round. Every switch shows the usual note — and if the category was
  already live, the note says so, so a key press never goes unanswered.
- **Suggested keys are filled in already.** Ctrl+Alt+1, 2, 3… where free, otherwise the same with
  Shift, otherwise Ctrl+Alt + a letter of the category's name; for next/previous Ctrl+Alt+Space /
  Shift+Space, otherwise PageDown/PageUp — each checked with Windows first, so only free keys are
  suggested. Other programs often hold every Ctrl+Alt+number already; then Work gets
  **Ctrl+Alt+W**, Home **Ctrl+Alt+H**, and so on.
- **To change one:** click its box and press **any key** — alone or with Ctrl, Alt, Shift or Win.
  Special keys work too: media keys (play/pause, volume, next track), **F13–F24**, mail/calculator/
  browser launch keys, Print Screen, Pause. **Clear** empties a line. While you record, Enter, Esc,
  Tab and Backspace are recorded like any other key; **Done** closes the window.
- **Active** — the tick in front of each name. Untick it to switch that one shortcut off: its keys
  are kept but do nothing, and other programs can use them again. The big *Turn on keyboard
  shortcuts* tick still switches all of them at once.
- **Reset** appears next to a shortcut once it differs from the one suggested, and puts that back.
- **In next / previous** — a tick per category. Next and Previous only stop at the ticked ones
  (all of them to begin with); a category's own shortcut works either way. If the live category is
  not ticked, they still move on from where it sits in the list.
- **Not every key can be used.** Logitech's **Easy-Switch keys** (1, 2, 3 — they choose which paired
  device the keyboard talks to) are handled inside the keyboard itself and never reach the PC; that
  is also why Logi Options+ cannot reassign them. The top-row keys and the ones at the top right
  usually can be given F13–F24 in Logi Options+.
- **A keyboard button that shows nothing** when pressed is handled by the keyboard's own software
  (for Logitech: Logi Options+). Give it a keystroke there — F13 to F24 are ideal, since no keyboard
  has them and nothing else uses them — then record that.
- **Each line says whether it will work:** *ready*, *taken by another program*, *used twice here*, or
  *types something* — a plain **K** types k, so while it is a shortcut you could not type k anywhere.
  That is allowed; it is your choice. On Polish and many other keyboards **Ctrl+Alt is AltGr** —
  Ctrl+Alt+A types ą — so those are never *suggested*, and are flagged the same way if you pick one.

## The window

Click the dock icon, or **Switch browser** on your Desktop.

- **Left** — your categories, and what each one points at. The live one is marked ●. Below them:
  **Show in dock** and **Dock icon…** for the selected category.
- **Right** — every browser on this computer, with its profiles underneath. Nothing is hard-coded:
  browsers come from the Windows registry, Firefox profiles from `profiles.ini`, and Chrome / Edge /
  Brave / Vivaldi profiles from each browser's own `Local State` file, so the names shown are the
  names you gave them.
- **Bottom** — one button per category. Click one and links go there from that moment on.

To set a category up: select it on the left, select a profile on the right, press **Use this for …**.

## Files

| | |
|---|---|
| **Switch browser** (Desktop) | opens the window |
| **Browser Switch** (Start menu) | opens the same window |
| **Browser Switch** (Startup folder) | starts the dock when you sign in. Delete it to stop that — nothing else depends on it |
| **Back to normal.cmd** | the panic button — turns every category off, so links go to your original default browser. Nothing is uninstalled and no category is lost |
| `config.txt` | your categories, in plain text. The window writes it; you can edit it by hand |
| **Install.cmd** | double-click to register Browser Switch with Windows — runs `install.ps1` |
| `install.ps1` | adds Browser Switch to the Windows list of browsers |
| `uninstall.ps1` | removes it completely |
| `build.cmd` | rebuilds the program |
| `BrowserSwitch.ico` | the icon — two opposite arrows — built into the exe |
| `LICENSE` | MIT — use, change and share it freely, keeping the copyright notice |

## Setting it up

Needs Windows 10 or 11.

1. **Get the program**, either way:
   - **Download** `BrowserSwitch-<version>.zip` from the
     [Releases page](https://github.com/Husarp/browser-switch/releases) and unzip it into a folder
     you will keep. The program is not signed, so Windows may say *"Windows protected your PC"* the
     first time — *More info → Run anyway*.
   - or **build it**: double-click `build.cmd`, which makes `BrowserSwitch.exe` with the C# compiler
     already inside Windows (.NET Framework 4). Nothing is downloaded.

   Keep the folder where it is afterwards: Windows is told to run the program from there.
2. **Double-click `Install.cmd`** from File Explorer. It runs `install.ps1` and waits for a key so
   you can read the result. Run it yourself: registry changes made from inside some programs — AI
   coding assistants included — land in a private copy of the registry that only that program sees,
   and Windows Settings would never see Browser Switch.

Browser Switch is **not** your default browser yet after that — Windows requires you to choose it
yourself. In Settings → Apps → Default apps there are two ways:

- **By app** — under *Set defaults for applications*, find **Browser Switch** (blue square, two
  arrows), open it, press **Set default**.
- **By link type** — in the top box, *Set a default for a file type or link type*, type `HTTP`, click
  the app shown under it, and choose **Browser Switch**. Then the same for `HTTPS`.

Until you do, nothing changes. The window says so across the top in yellow whenever Browser Switch
is not the default, and its button opens that page for you.

**If Browser Switch is not offered:** double-click `Install.cmd` again — yourself, not from inside
another program — then close Settings completely (clicking the X only hides it) and open it again.
What `install.ps1` sets up, and why:

- **the registration** — a link handler, `BrowserSwitchURL`, and an entry in `RegisteredApplications`
  saying it handles http, https, .htm and .html. This is what puts it in Windows' list of apps for
  links;
- **an identity** — a name, an icon and a publisher, both inside the exe and on the link handler, and
  one app ID, `BrowserSwitch`, shared by the link handler, the client key and the Start menu
  shortcut. Before 2.2.0 the exe had no icon and a blank name, so the most Windows could call it was
  "BrowserSwitch.exe";
- **a Start menu shortcut**, and an entry in Settings → Apps → Installed apps, so it can be found and
  removed like any other program.

## If something goes wrong

A link must never die because the switcher is confused. Where a link goes is worked out in one place,
and `--dry` reports exactly what a real click would do, so the two can never disagree:

1. the live category, if its browser still exists;
2. otherwise the browser that was your default **before** Browser Switch was installed — recorded at
   install time, so a machine with nothing set up behaves exactly as it did before;
3. otherwise the first browser Windows lists.

Anything unexpected is written to `errors.log`. Tested against: no config at all, a config with no
categories, a live category with no browser chosen, and a category pointing at a browser that has
been deleted — all four fall through correctly.

Two levels of undo, on purpose:

- **Pause** — `Back to normal.cmd`. Instant, nothing removed, one click away from switching back on.
- **Remove** — set a real browser as default in Settings, then run `uninstall.ps1`. Every registry
  key it created lives under your own user account and is deleted. `uninstall.ps1` refuses to run
  while Browser Switch is still the default and opens Settings instead, so Windows is never left
  pointing at something that no longer exists. Browser Switch also appears in Settings → Apps →
  Installed apps, and removing it there runs the same script.

## Command line

```
BrowserSwitch.exe <url>          open a link in the live category
BrowserSwitch.exe                open the window (starting the dock if it is not running)
BrowserSwitch.exe --tray         start the dock only - what runs when you sign in
BrowserSwitch.exe --switch Work  make a category live, no window
BrowserSwitch.exe --reset        no category live; links go to the original default browser
BrowserSwitch.exe --dry <url>    write where the link WOULD go to dry-run.log, open nothing
BrowserSwitch.exe --list         write the browsers and profiles it can see to detected.txt
BrowserSwitch.exe --selftest     build the window in memory and report what it holds, without showing it
```

## One caveat, with Firefox

Firefox is launched with `-P "profile name"`. If Firefox is already running with a different profile,
it may open the link in the window that is already there rather than starting the profile you asked
for. Chrome, Edge and Brave handle `--profile-directory` cleanly whether or not they are running.
