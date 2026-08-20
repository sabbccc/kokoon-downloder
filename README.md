# Kokoon Downloader

A native Windows download manager (IDM/ADM-style) built with WinUI 3 on .NET 8 — multi-segment
parallel HTTP downloading with resume support, plus a bundled yt-dlp-powered video grabber for
YouTube and thousands of other video sites.

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-6C5CE7)
![.NET](https://img.shields.io/badge/.NET-8-6C5CE7)
![License](https://img.shields.io/badge/license-MIT-6C5CE7)

## Features

- **Multi-segment acceleration** — splits files into up to 32 parallel connections, with automatic
  fallback to single-stream when a server doesn't support byte ranges
- **Resume support** — segment byte-ranges persist to disk, so a paused or interrupted download
  picks up exactly where it left off, even after closing the app
- **Video grabbing** — probe and download from YouTube or any of yt-dlp's 1800+ supported sites,
  with a dynamic quality picker built from whatever formats are actually available per video
- **Browser-login handoff** — for sites that need an authenticated session, log in via your default
  browser once and Kokoon remembers it for that domain going forward
- **Automatic yt-dlp updates** — checked and applied silently in the background on launch
- **Queue management, speed limiting, and a system tray presence** with pause/resume-all controls

## Installation

Grab the latest release and either:

- **Installer** — run the `.exe`, follow the prompts.
- **Portable** — extract the `.zip` anywhere and run `Kokoon.UI.exe` directly. No install, no admin
  rights; settings and history live under `%LOCALAPPDATA%`/`%APPDATA%`, not the app folder.

See the [User Guide](docs/USER_GUIDE.html) for full usage instructions, and
[System Requirements](docs/USER_GUIDE.html#s2) for what's needed.

## Building from source

Requires the .NET 8 SDK (`global.json` pins `8.0.0`) and Visual Studio with the "Windows
application development" workload (for the WindowsAppSDK/Appx build tools `Kokoon.UI` needs, even
though it's unpackaged).

```powershell
dotnet build KokoonDownloader.slnx
```

`Kokoon.UI` specifically needs an explicit platform:

```powershell
dotnet build Kokoon.UI/Kokoon.UI.csproj -p:Platform=x64 -p:Configuration=Debug
```

For a self-contained Release publish and portable zip, see the
[publish & bundling guide](docs/publish-portable-guide.html) — a full walkthrough with
copy-pasteable PowerShell.

## Project structure

```
KokoonDownloader.slnx
  Kokoon.Core            .NET 8 class library     — HTTP download engine, queue/scheduler, SQLite persistence
  Kokoon.VideoGrabber    .NET 8 class library     — yt-dlp/ffmpeg wrapper for video sites
  Kokoon.UI              WinUI 3 app (WinExe)     — the desktop application
```

- **`Kokoon.Core`** has zero UI dependency: the HTTP engine, in-memory scheduler, and EF Core/SQLite
  persistence layer.
- **`Kokoon.VideoGrabber`** wraps the bundled `yt-dlp.exe`/`ffmpeg.exe` (under
  `Kokoon.VideoGrabber/tools/`) for URLs the segment engine can't handle directly — HLS/DASH
  streams, video sites in general.
- **`Kokoon.UI`** is the only project that builds for `x86`/`x64`/`arm64`; the other two are plain
  AnyCPU libraries.

More architectural detail lives in [CLAUDE.md](CLAUDE.md).

## Tech stack

.NET 8 · WinUI 3 (unpackaged, `WindowsPackageType=None`) · Entity Framework Core + SQLite ·
CommunityToolkit.Mvvm · yt-dlp + ffmpeg (bundled)

## Documentation

| Doc | Purpose |
|---|---|
| [User Guide](docs/USER_GUIDE.html) | Installing, configuring, and using the app |
| [Publish & Portable Bundle Guide](docs/publish-portable-guide.html) | Copy-pasteable PowerShell for the self-contained publish + portable zip pipeline |
| [Commit & Push Guide](docs/commit-and-push-guide.html) | One-time GitHub setup and everyday git workflow for this repo |

## License

[MIT](LICENSE)
