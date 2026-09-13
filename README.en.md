# 🎬 BrowserVideoGrabber

> Browsing videos online but left with nothing to show for it? **BrowserVideoGrabber** makes it dead simple. Built-in browser opens any page, video resources auto-populate the sniff list, double-click to download. Too many tabs? Open more with a single click. Love a page? Tap ⭐ and save it for later.

<p align="center">
  <em>Built with ❤️ — .NET 10 · Windows Forms · WebView2 (Edge Chromium)</em>
</p>

<p align="center">
  <a href="README.md">🇨🇳 中文</a> · <a href="README.en.md">🇬🇧 English Version</a>
</p>

---

## ✨ What can it do?

| Capability | Description |
|---|---|
| 🕵️ In-page video sniffing | Three-layer detection: network response listening + URL signature matching + JS hook injection — catches direct links, dynamic URLs, and MSE fragments |
| 🧩 One video, one row | Playlists, quality variants, and thousands of TS segments all **collapse into a single row** — clean UI, full-file download |
| 📦 Multi-format download | `m3u8` / `ts` / `m4s` / `mpd` (DASH) → ffmpeg; `mp4` → native HTTP multi-threaded segmented download for speed |
| ⏸️ Resume after interruption | MP4 uses `.partN` segments — resume only fetches what's missing, never starts over |
| 🍪 Session reuse | Auto-exports Referer / UA / Cookie from the built-in browser — works on pages that require login |
| 📑 Tabbed browsing | `<a target="_blank">` opens a **new tab** (not a separate window), sharing session state and login cookies |
| ⭐ Favorites | Tap ⭐ on any page to bookmark it; manage favorites from the dedicated form |
| 🎛️ Queue scheduling | Configurable concurrency, exponential backoff retries, pause / resume / cancel at any time |
| 💾 Persistent state | Tasks and settings are auto-serialized — **restart and pick up where you left off** |
| 🔧 Self-check on startup | Missing ffmpeg? An orange banner appears at the top, the status bar shows a hint, and install guidance is provided |

### 🔐 Encryption boundary (please read before use)

| Scenario | Supported? |
|---|---|
| Unencrypted videos | ✅ Yes |
| `#EXT-X-KEY:METHOD=AES-128` | ✅ ffmpeg fetches the key and decrypts automatically |
| `SAMPLE-AES` / `SESSION-KEY` (DRM) | ❌ **No** — fails immediately with a clear message, **no pointless retries** |

> This tool does not circumvent DRM protection. It downloads only unprotected content. Please respect copyright laws and the terms of service of the websites you use.

---

## 🧰 Requirements

| Component | Required |
|---|---|
| OS | Windows 10 1809+ / Windows 11 |
| .NET SDK | **10.0** (for building); runtime needs .NET 10 Desktop Runtime |
| WebView2 Runtime | Pre-installed on Win10/11; if missing, install [Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) |
| ffmpeg | **Optional but strongly recommended**. Without it, m3u8/mpd/ts/m4s downloads fail; MP4 direct links still work |

---

## 📥 Installing ffmpeg

The app does **not ship ffmpeg** (large binary, licensing and maintenance overhead). Instead it probes for ffmpeg at runtime in this priority order:

1. Manually-specified path in Settings
2. `tools\ffmpeg\ffmpeg.exe` under the app directory (for portable packaging)
3. `ffmpeg.exe` directly under the app directory
4. System `PATH` environment variable
5. Common locations: `%USERPROFILE%\scoop\shims`, `C:\ProgramData\chocolatey\bin`, `C:\ffmpeg\bin`

### Recommended install methods

```powershell
# Easiest (pick one: winget / scoop / choco)
winget install Gyan.FFmpeg
# or
scoop install ffmpeg
# or
choco install ffmpeg
```

**Manual download**: Grab `ffmpeg-release-essentials.zip` from [gyan.dev](https://www.gyan.dev/ffmpeg/builds/), extract anywhere, add the `bin` folder to system `PATH`, then **restart BrowserVideoGrabber**.

**Portable**: Drop `ffmpeg.exe` into `tools\ffmpeg\` under the app directory — no PATH modifications needed.

Once the app starts, the bottom-right status bar shows `ffmpeg: <path>`. If not found, an orange banner appears at the top.

---

## 🏗️ Build & Run

```powershell
# Build (single project — consolidated from 3 sub-projects)
dotnet build src/BrowserVideoGrabber/BrowserVideoGrabber.csproj -c Release

# Run unit tests (265 tests across Core & Infrastructure layers)
dotnet test tests/BrowserVideoGrabber.Core.Tests/

# Launch
.\src\BrowserVideoGrabber\bin\Release\net10.0-windows\BrowserVideoGrabber.exe
```

> Note: The old `.slnx` solution file has been removed. Build directly from the csproj.

---

## 🚀 Five-minute quick start

### What the UI looks like

```
┌─ Tab bar ← →  ⟳  ☆  [ Address bar (spring-filled) ]  GO  ⏹ ─────────────────┐
│                                                                                │
│   Left: Built-in browser (tabbed)  │        Top-right: Sniff panel           │
│                                     │        Format / Quality / Name / URL     │
│                                     ├─────────────────────────────────────────┤
│                                     │        Bottom-right: Download queue      │
│                                     │        Pending / Running / Done          │
│                                     │        Pause · Resume · Cancel · Open    │
├────────────────────────────────────────────────────────────────────────────────┤
│  Ready / Loading…                                             ffmpeg: Ready     │ ← Status bar
└────────────────────────────────────────────────────────────────────────────────┘
```

### Typical flow

1. Launch the app, type a URL in the address bar (or just keywords — auto-Bing search kicks in)
2. **Log in if the site requires it** (critical! Session cookies are reused for download requests)
3. Play a video. Wait a few seconds — resources pop up in the sniff panel on the right 👀
4. **Double-click** any entry → added to download; or right-click and pick "Add to download"
5. Switch to "Running" at bottom-right to watch progress; double-click "Done" entries to open the file directly

### Pro tips

| Scenario | How to |
|---|---|
| Open a link in a new tab | `<a target="_blank">` opens a new tab automatically; or right-click the tab bar to create one |
| Bookmark the current page | Tap the ☆ button in the toolbar; manage favorites from the dedicated form |
| Reset and re-sniff | Right-click the sniff panel → "Clear list", then refresh and replay the video |
| Resume a broken download | Click "Resume" — MP4 picks up from the last successfully downloaded segment |
| ffmpeg not found? | Check the status bar → open Settings → auto-detect or specify manually |

### Settings at a glance

| Item | Description |
|---|---|
| ffmpeg path | Leave blank for auto-detect (recommended) |
| Output directory | Downloads land in `%USERPROFILE%\Downloads` by default |
| Concurrent downloads | Default 3; too high may trigger rate-limiting |
| MP4 segment count | Default 4; set to 1 for single connection |
| User-Agent | Leave blank to inherit the browser's current UA |
| Auto-sniff on startup | Toggle on/off; manual toggle button always available |

> Changing concurrency or MP4 segment count rebuilds the download pipeline. Currently running tasks are cancelled and re-queued automatically.

### Where are the files?

| What | Default location |
|---|---|
| Settings | `%LOCALAPPDATA%\BrowserVideoGrabber\settings.json` |
| Download tasks | `%LOCALAPPDATA%\BrowserVideoGrabber\tasks.json` |
| Favorites / History | `favorites.json` / `history.json` in the same directory |
| Logs | `%LOCALAPPDATA%\BrowserVideoGrabber\logs\app.log` |
| Browser cache / cookies | `%LOCALAPPDATA%\BrowserVideoGrabber\WebView2` |

There's an "Open app data folder" button in the toolbar that jumps right there.

---

## 🧪 Manual smoke checklist (26 items)

265 unit tests cover sniff matching, m3u8 parsing, ffmpeg argument building, queue scheduling, segmented download & resume, and other pure-logic layers. **UI behavior and real-website interaction need manual confirmation.** Suggested checklist:

| # | Test case | Expected |
|---|---|---|
| 1 | Double-click to run | Main window renders cleanly, no error dialogs |
| 2 | Check status bar | Shows ffmpeg path when installed; shows "not found" with orange banner when missing |
| 3 | Type `bing.com` and Enter | Address auto-prefixed with `https://`, page loads |
| 4 | Type "test" and Enter | Triggers search, not treated as a URL |
| 5 | Back / Forward buttons | Auto-disabled when no history available |
| 6 | Play a page with MP4 direct links | MP4 entry appears at top-right, source shows "Network" or "Script" |
| 7 | Play an HLS (m3u8) page | **Exactly one row per video** (playlist + quality variants + thousands of TS segments auto-merge) |
| 8 | Double-click an MP4 sniff result | Progress bar advances to 100%, file appears in output directory |
| 9 | Pause mid-download | Task moves to "Pending — Paused", network traffic stops |
| 10 | Resume | Download picks up and completes |
| 11 | Download an m3u8 task | ffmpeg spawned, progress advances by timestamp, output is a playable mp4 |
| 12 | Disconnect and reconnect network mid-download | Auto-retry; already-downloaded segments kept — **no restart from scratch** |
| 13 | Cancel mid-download | No leftover `.part` / `.assembling` files in output directory |
| 14 | Download DRM-protected content | Fails immediately with clear reason, no retry storm |
| 15 | Download from a page that requires login (after logging in) | Succeeds (Cookie / Referer injection working) |
| 16 | Double-click a completed task in "Done" tab | System default media player opens the file |
| 17 | Right-click → "Open file location" | Explorer navigates to the file |
| 18 | Close and re-open | Download list + previously-opened tabs restored |
| 19 | Change concurrency to 1 | Setting applies; only one task runs at a time |
| 20 | Enter a nonexistent ffmpeg path manually | Confirmation dialog warns about missing path |
| 21 | Start sniffing **after** playback has already begun | May initially catch only TS segments; attempting download prompts "only partial video" |
| 22 | Refresh and replay (continuation of #21) | Original segment row **upgrades in place** to an M3U8 row (no duplicate rows) |
| 23 | Remove a task from "Pending" via right-click | Row disappears, does not resurrect on next launch |
| 24 | Right-click a "Running" row | "Remove from list" is disabled (must cancel first) |
| 25 | Inspect a failed task in "Done" | "Reason" column shows specifics (missing Referer / Cookie hint, etc.), not blank |
| 26 | Clear sniff list, replay same video | **Re-sniffed successfully** (clear also flushes the dedup table) |

---

## 🏛️ Project structure (consolidated, single project)

```
src/BrowserVideoGrabber/
├── Program.cs                      # Entry point + exception persistence
├── BrowserVideoGrabber.csproj      # Single project: .NET 10 WinForms + WebView2
├── AppHost.cs                       # Hand-written composition root (DI without container)
├── AboutForm.cs                     # About dialog + usage disclaimer
│
├── App/                             # UI layer
│   ├── Forms/MainForm.cs            # Main window
│   ├── Forms/FavoritesForm.cs       # Favorites manager
│   ├── Forms/HistoryForm.cs         # Browsing history
│   ├── Forms/SettingsForm.cs        # Settings dialog
│   ├── Panes/BrowserPane.cs         # Browser container (tab management + toolbar)
│   ├── Panes/BrowserTab.cs          # Individual WebView2 tab
│   ├── Panes/BrowserTabStrip.cs     # Custom-drawn tab strip
│   ├── Panes/SniffPane.cs           # Sniff list panel
│   ├── Panes/DownloadPane.cs        # Download queue panel
│   ├── Binding/                     # List binding with progress throttling
│   └── Controls/BufferedListView.cs # Double-buffered list control
│
├── Core/                            # Domain layer (zero UI deps, home of unit tests)
│   ├── Models/                      # SniffedVideo · DownloadTask · DownloadProgress …
│   ├── Abstractions/                # IVideoSniffer · IDownloadHandler · IProcessRunner …
│   ├── Sniffing/                    # VideoUrlMatcher (URL + Content-Type classification)
│   ├── Downloads/                   # VideoFamilyIndex · HlsPlanBuilder · M3u8Parser …
│   ├── Ffmpeg/                      # FfmpegArgumentBuilder · FfmpegProgressParser
│   └── Common/                      # Result · RetryPolicy
│
└── Infrastructure/                  # Infrastructure layer (talks to the real world)
    ├── Sniffing/WebView2Sniffer.cs  # Coordinates three sniffing pipelines
    ├── Sniffing/JsHookInjector.cs   # Injects JS to capture dynamic URLs
    ├── Downloads/HttpDownloadHandler.cs    # MP4 multi-threaded segmented downloader
    ├── Downloads/FfmpegDownloadHandler.cs  # m3u8 / mpd handler
    ├── Ffmpeg/FfmpegLocator.cs              # ffmpeg auto-detection
    ├── Security/CookieExporter.cs           # Cookie / UA / Referer extraction from WebView2
    ├── Execution/ProcessRunner.cs           # ffmpeg process management
    └── Storage/JsonTaskRepository.cs        # Tasks / Settings / Favorites / History persistence
```

### Dependency direction

```
App (UI) ──▶ Infrastructure ──▶ Core (domain models + pure algorithms)
   ▲                                │
   └────────────────────────────────┘
      Infrastructure implements Core's interfaces
```

`Core` references **no** UI or platform types. Every piece of logic there — sniffing rules, m3u8 parsing, ffmpeg argument building, queue scheduling, segmented download & resume — can be unit-tested with mock implementations, deterministically, no network, no disk, no real processes.

---

## ⚠️ Known limitations

| Limitation | Notes |
|---|---|
| DRM-protected content | **Not supported** (by design) |
| `blob:` URLs | MSE playback may not expose a downloadable URL; JS hooks attempt prefix reconstruction but success is not guaranteed |
| Time-signed URLs | Some sites invalidate URLs quickly — sniff and download promptly. If it fails, replay the page and re-sniff |
| Referer accuracy | Uses the top-level browser document URL. Sites validating an iframe-internal URL may return 403 — the UI hints at a Referer/Cookie check |
| Huge sniff lists | Tracks up to 800 videos; oldest entries get evicted when the cap is hit (new resources still reported normally) |
| Only fragments captured | If sniffing is enabled **after** playback has started, you may only get TS segment entries. Refresh and replay to capture the playlist |
| Live streams | HLS live streams (no `#EXT-X-ENDLIST`) keep downloading until you cancel |

---

## 🙋 FAQ

**Q: Double-clicking the app does nothing / flashes and closes?**
Check `%LOCALAPPDATA%\BrowserVideoGrabber\logs\app.log` for the full exception stack trace.

**Q: WebView2 initialization failed?**
Install the [Microsoft Edge WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) and retry.

**Q: MP4 downloads fine but m3u8 keeps failing?**
First check the status bar — is it "ffmpeg: not found"? If ffmpeg is installed, the site likely validates Referer / Cookie — log in via the built-in browser, refresh the page, replay the video, and re-sniff.

**Q: Downloads are slow?**
m3u8 through ffmpeg is single-threaded sequential — speed depends on the site's rate limiting. For MP4, try increasing the segment count in Settings. But too high concurrency triggers rate limits, making things slower.

**Q: Why not just use the browser's "Save video as"?**
Browsers often hide that menu item; plus browsers can't download m3u8 streams. BrowserVideoGrabber sniffs at the **network layer** and works regardless of what the front-end hides.

**Q: Is it legal to use this to study a video platform's anti-scraping mechanisms?**
Please respect the target site's terms of service and the copyright laws of your jurisdiction. This tool downloads only **unprotected** content and does not bypass encryption or anti-tampering measures.

---

<p align="center">
  <sub>🎥 BrowserVideoGrabber · Keep it sniffer-y</sub>
</p>
