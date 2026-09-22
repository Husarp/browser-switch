# CHANGELOG — Browser Switch

All notable changes to this project are listed here. Newest on top.
Format: `X.Y.Z — YYYY-MM-DD HH:MM: <description>`
- X = major overhaul, Y = new feature/update, Z = minor fix or tweak

---

## 1.0.0 — 2026-09-22 12:10: First version

One click sends every link on the computer to Firefox or to Chrome, for switching to the work desktop
without pasting links between browsers.

Windows refuses to let a script change the default browser: the setting carries a signature tied to
the account, and anything written directly is thrown away. That protection stops browser hijacking
and is worth keeping. So this does not fight it — **Browser Switch becomes the default browser**,
reads one word from `mode.txt`, and passes the link straight on to the real browser.

- 8 KB, compiled from `BrowserSwitch.cs` by the C# compiler already inside Windows. Nothing is
  downloaded, and the source is readable in full.
- `browsers.txt` decides which browser each mode means — change a path and it applies at once, with
  no rebuild.
- A label appears near the clock for a second on each switch, then removes itself. The mode is
  written *before* the label is drawn, so switching works even if the label ever fails.
- Every failure falls back to the personal browser: missing `mode.txt`, unreadable `browsers.txt`, an
  uninstalled browser. A broken switcher must never mean a dead link. Anything unexpected goes to
  `errors.log`.
- Two levels of undo: `Back to Firefox.cmd` forces everything to Firefox instantly without removing
  anything, and `uninstall.ps1` deletes every registry key it created. `uninstall.ps1` refuses to run
  while Browser Switch is still the default and opens Settings instead, so Windows is never left
  pointing at something that no longer exists.
- Registration is per-user under `HKEY_CURRENT_USER`, so no administrator rights are needed.

Tested by resolving links without opening anything (`--dry`), including with `mode.txt` deleted and
`browsers.txt` missing; both fall through to Firefox correctly.
