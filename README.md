# Browser Switch

One click sends every link on this computer to Firefox or to Chrome. Meant for switching to the work
desktop without pasting links between browsers.

## Why it is built this way

Windows will not let a script change your default browser. The setting is stored with a signature
tied to your account, and anything that writes it directly is thrown away. That protection is there
to stop programs hijacking your browser, and it is worth keeping.

So this does not fight it. **Browser Switch becomes the default browser.** When you click a link
anywhere, Windows hands it to this program, which reads one word from `mode.txt` and passes the link
straight on to the real browser. Flipping that word is the switch.

Nothing was downloaded to build this. `BrowserSwitch.exe` is 8 KB, compiled from `BrowserSwitch.cs`
by the C# compiler that is already inside Windows (`build.cmd` rebuilds it).

## Using it

| | |
|---|---|
| **Switch browser** (on your Desktop) | flip between personal and work. A label appears near the clock for a second so you know which mode you are in |
| **Back to Firefox.cmd** | the panic button — forces everything to Firefox instantly, uninstalls nothing, and the Desktop shortcut still works afterwards |
| `browsers.txt` | which browser each mode means. Change a path and it takes effect at once |
| `install.ps1` | adds Browser Switch to the Windows list of browsers |
| `uninstall.ps1` | removes it completely |

## Setting it up

`install.ps1` has already been run, so Browser Switch appears in Windows Settings. It is **not** your
default browser yet — Windows requires you to choose that yourself:

> Settings → Apps → Default apps → **Browser Switch** → set it for HTTP and HTTPS

Until you do, nothing changes.

## If something goes wrong

It starts in personal mode, so behaviour is identical to having Firefox as default until you flip it.

Every failure falls back to opening the link in the personal browser — a missing `mode.txt`, an
unreadable `browsers.txt`, a browser that has been uninstalled. A broken switcher must never mean a
dead link. Anything unexpected is written to `errors.log`.

Two levels of undo, on purpose:

- **Pause** — double-click `Back to Firefox.cmd`. Everything behaves as before, still installed, one
  click away from switching back on.
- **Remove** — set Firefox as your default in Settings, then run `uninstall.ps1`. Every registry key
  it created lives under your own user account and is deleted. Run `install.ps1` to bring it back.

`uninstall.ps1` refuses to run while Browser Switch is still your default, and opens the Settings page
for you instead — otherwise Windows would be left pointing at something that no longer exists.
