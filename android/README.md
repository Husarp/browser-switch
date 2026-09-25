# LinkPilot for Android

The Android companion of [LinkPilot for Windows](https://github.com/Husarp/linkpilot): it
stands in as your default browser and hands every link on to the browser you chose, with tracking
taken out on the way. Switch where links go with one tap on a Quick Settings tile.

## What it does

- **Setup screen** — as on Windows: how it works and why it can be trusted, your choices (cleaning,
  log), making it the default browser (Android's own question), and "You're all set". It shows on
  the first start and whenever LinkPilot is not the default browser; *Show the setup again* is
  on Home.
- **Categories** — Work, Home… each is one of the browsers on your phone. The live one gets your
  links. On the first start, the browser you used until then becomes the first category.
- **Quick Settings tiles** — two, for the panel you pull down from the top; add either or both:
  - **Next browser** — each tap makes the next category live (a short message says which).
  - **Choose browser** — a tap shows the list of categories to pick from.

  Both show the live category's name; a long press opens the app. **Change icon** picks each tile's
  icon from twelve (Android draws tile icons in one colour, so they are simple shapes). *Add* asks
  Android to place a tile - its question, "Add tile / Do not add tile", is Android's own wording.
- **Home-screen widgets** — the same two, *Next browser* and *Choose browser*, showing the live
  category with its browser's icon. *Add* on Home places one.
- **Link rules** — links from chosen apps (Signal → Work) or to chosen sites (github.com → Home) go
  to their own category, whatever is live. *Use rules* switches them all off and on.
- **Link cleaning** — the same as on Windows: 110 tracking parts, online shops included (Amazon, eBay,
  AliExpress, Allegro, Temu, Shein, Etsy...; `utm_*`, `fbclid`, YouTube's
  `si`…) removed and 15 redirects (`google.com/url?q=…`, Facebook's `l.php`, Outlook Safe Links…)
  skipped. Each has its own tick; **Try a link** shows what a link becomes. When a link had something
  taken out, a short message says so ("Link cleaned - removed utm_source").
- **Copied links** — **Clean copied links automatically** (Cleaning tab, off to begin with): copy a
  link with tracking and it is cleaned right away, so you paste the clean one. Android lets an app see
  what was copied only while it is on screen, so this is done by an accessibility service that
  listens to **one app only - the system's own "copied" preview** (Android 13+ shows it for every
  copy, with what was copied), never to other apps, and cannot read the screen. It has to be switched
  on once in Android's Accessibility settings (the app shows the way). Android 8-9: a plain clipboard
  listener. **Android 10-12 give no sign of a copy**, so there it is on a tap only. Always also on a
  tap: the **Clean copied link** Quick Settings tile, *Clean the copied link now*, or share a link to
  LinkPilot and pick *Copy clean link*. Only a link on its own is changed; what a password
  manager marks private is left alone.
- **Browsers in the work profile** (Island, or a work phone's profile) — a category can open its
  links in a browser over there, badged with the work briefcase. Needs Android 11+, **LinkPilot
  installed in both profiles** (Island: clone it - the setup and Home's (i) say so), and permission
  to connect the two copies. Android
  gives that switch only if the work profile's app lists LinkPilot as a "connected app"; Island
  does not, so it is allowed once from a computer:
  `adb shell appops set com.husarp.linkpilot INTERACT_ACROSS_PROFILES allow`.
  *Add a category* then lists the work profile's browsers (by name - Android does not tell one
  profile what the other's apps open; *Show all its apps* shows the rest).
- **Link log** — every link, grouped by day: the app it came from (its icon), the site, the time,
  where it went and whether it was cleaned. Tap one for the whole link, what it came as, what was
  taken out and why it went there, with **Copy link** and **Open again**. The newest 300, on the
  phone only; can be switched off and cleared.

## How it differs from Windows

| | Windows | Android |
|---|---|---|
| A category is | a browser **and profile** | a browser (Android browsers have no profiles another app can choose) |
| Switching | the dock, keyboard shortcuts | Quick Settings tiles, home-screen widgets, the app |
| Cleaning copied links | by itself, when you copy | by itself on Android 13+ (and 8-9), via an accessibility service that hears only the system's "copied" preview; else on a tap |
| Which app a link came from | always known | known when the app says so (most do) |

## Privacy

**It cannot go online:** the app does not ask for the internet permission, so Android itself keeps
it offline. Your categories, rules and link log stay in the app's own storage on your phone.

## Limits

Some links never reach any default browser, so LinkPilot cannot see them either:

- apps that open links in their **own built-in browser** (Instagram, Facebook, some others);
- some **Google apps**, which always open links in Chrome;
- links an app opens itself — a YouTube link goes straight to the YouTube app, if it is installed.

## Install

Download **`LinkPilot-Android-<version>.apk`** from the
[Releases page](https://github.com/Husarp/linkpilot/releases) on your phone and open it - the steps
are in the main README, [Install on Android](../README.md#install-on-android). Or build it yourself
from this folder (see [Building](#building)).

Then, in the app: **Make it the default** — Android shows its own "Set as default browser?"
question; no app can do this without you.

**Updates**: Home › About › **Check for updates on GitHub** opens the newest release in your browser
(LinkPilot cannot check by itself: it has no internet permission). Download the APK there if it is
newer than yours.

## Building

- Android Studio (its own Java), Android SDK 35.
- `gradlew assembleRelease` → `app/build/outputs/apk/release/app-release.apk` — optimised, about
  1 MB; use this one on a phone (a debug build scrolls noticeably slower). It is signed with the
  release key named in `~/.keystores/linkpilot-signing.properties` (`storeFile`, `storePassword`,
  `keyAlias`, `keyPassword`) if that file exists - only the author's PC has it - and with your own
  debug key otherwise. An APK signed with another key cannot be installed over the published one.
- `gradlew assembleDebug` → `app/build/outputs/apk/debug/app-debug.apk` — for testing.
- `gradlew testDebugUnitTest` — link cleaning checked against the same known answers as the Windows
  app, and address rules.

Kotlin, Jetpack Compose (Material 3); no other libraries. Minimum Android 8.0.

## Files

| File | What it is |
|---|---|
| `Cleaner.kt` | link cleaning — the same lists and rules as the Windows `Cleaner.cs` |
| `Router.kt` | which category a link goes to: rules first, then the live category |
| `Store.kt` | settings and the link log, on the phone |
| `Browsers.kt` | the browsers and apps on the phone; whether LinkPilot is the default |
| `LinkActivity.kt` | receives every link and hands it on — nothing shows |
| `ShareActivity.kt` | Share → *Copy clean link* |
| `Tiles.kt` | the two Quick Settings tiles, and their icons |
| `Widgets.kt` | the two home-screen widgets, and the list *Choose browser* shows |
| `Clipboard.kt` | *Clean copied link*: the tile, and the moment on screen it needs to read |
| `CopyWatch.kt` | copied links cleaned by themselves - the accessibility service |
| `Profiles.kt` | browsers in the work profile, and handing links to LinkPilot there |
| `Setup.kt` | the setup screen |
| `MainActivity.kt` | the app: Home, Rules, Cleaning, Log |

## Licence

MIT — see [LICENSE](../LICENSE).
