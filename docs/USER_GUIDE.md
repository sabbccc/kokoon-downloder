# Kokoon Downloader — User Guide

A complete guide to installing, setting up, and using Kokoon Downloader.

> **Accuracy note**: this guide matches the current icon-dock sidebar, the purple-accented theme with a Dark/Light/System mode picker, the yt-dlp-powered Video Grabber's dynamic quality dropdown, browser-login handoff for authenticated sites, and automatic yt-dlp updates. If something drifts from what you see on screen, the app is ground truth, not this doc.

## Table of Contents

1. [What is Kokoon Downloader?](#1-what-is-kokoon-downloader)
2. [System Requirements](#2-system-requirements)
3. [Installation](#3-installation)
4. [First Launch](#4-first-launch)
5. [The Main Interface](#5-the-main-interface)
6. [Adding Downloads](#6-adding-downloads)
7. [Downloading Videos](#7-downloading-videos)
8. [Managing Downloads](#8-managing-downloads)
9. [Settings](#9-settings)
10. [System Tray](#10-system-tray)
11. [Troubleshooting](#11-troubleshooting)
12. [Data Storage](#12-data-storage)

---

## 1. What is Kokoon Downloader?

Kokoon is a modern download manager for Windows with:

- **Multi-segment acceleration** -- splits files into parallel connections for faster downloads
- **Video grabbing** -- downloads videos from YouTube and thousands of other sites via yt-dlp
- **Queue management** -- schedules and prioritizes downloads
- **Resume support** -- pause and resume downloads even after closing the app

---

## 2. System Requirements

- Windows 10 version 1809 (build 17763) or later
- Windows 11 (any version)
- x64 processor (ARM64 supported if built for that platform)
- No additional runtime installation needed (self-contained build)

---

## 3. Installation

### Option A: Installer (Recommended)

1. Download `KokoonDownloader-Setup-x.x.x.exe`.
2. Run the installer. If Windows SmartScreen warns you, click **More info** > **Run anyway**.
3. Choose your installation folder (default: `C:\Users\<you>\AppData\Local\Programs\Kokoon Downloader`).
4. Optionally check **Create a desktop shortcut** and/or **Start Kokoon with Windows**.
5. Click **Install**, then **Finish**.

### Option B: Portable

1. Download `KDM-Portable-win-x64.zip`.
2. Extract the zip to any folder (e.g. `C:\Tools\KokoonDownloader`).
3. Run `Kokoon.UI.exe`.
4. No installation is needed. Your settings and download history are stored under `%LOCALAPPDATA%\KokoonDownloader\` and `%APPDATA%\KokoonDownloader\` (see [Section 12](#12-data-storage)), not in the app folder.

---

## 4. First Launch

1. The app creates its data directories at `%LOCALAPPDATA%\KokoonDownloader\` (database, logs) and `%APPDATA%\KokoonDownloader\` (settings).
2. You'll see the main window with an empty download list and a prompt to add your first download.

---

## 5. The Main Interface

### Left Sidebar

A slim icon-only dock with just three destinations, top to bottom:

- **Logo** -- the app icon (not clickable)
- **Downloads** -- the app's only page; always selected. A small dot lights up while at least one download is active.
- **Video Grabber** (camera icon) -- opens a dedicated dialog for grabbing video from any yt-dlp-supported site.
- **Settings** (gear icon, bottom) -- opens the Settings page.

Each icon shows a tooltip on hover.

### Header and Main Content Area

The header has filter tabs -- **All / Active / Queued / Completed / Failed** -- each with a live count, plus a **+ New** button (top-right) that opens Add Download.

Below that, download cards for the selected filter show:
- File icon and name
- Source domain and file size
- Status badge (Downloading, Queued, Completed, Failed, Paused)
- Progress bar with percentage, speed, and ETA
- Segment visualization (colored bars per connection, for direct-file downloads)
- Action buttons (pause, resume, cancel, open file, open folder)

### Status Bar (Bottom)

Shows active count, queued count, and total download speed at a glance.

---

## 6. Adding Downloads

1. Click **+ New** in the top-right of the Downloads page (or use the button in the empty state).
2. Paste or type a URL in the URL field.
3. Click **Analyze**. Kokoon probes the URL for file name, size, range-request support, and whether it's a video.
4. Review the detected info -- edit the filename, adjust the **Segments** slider (1-32 parallel connections), and choose a save folder.
5. Click **Download Now** to start immediately, or **Queue** to schedule it.

---

## 7. Downloading Videos

Kokoon has built-in video grabbing powered by yt-dlp, which supports YouTube and thousands of other video sites (Vimeo, Twitter/X, Reddit, Dailymotion, Facebook, Instagram, TikTok, and more).

There are two ways to reach it: a dedicated **Video Grabber** dialog (camera icon in the sidebar), and inline detection inside the Add Download dialog.

### How It Works

1. Open **Video Grabber** from the sidebar, or paste a video page URL into **Add Download** and click **Analyze**.
2. Once probed, you'll see a thumbnail, title, uploader, and duration, plus a **Quality dropdown** -- listing every format yt-dlp actually found for that specific video (this varies per video and per site; there's no fixed list of resolutions). Pick whichever entry you want.
3. The **Use yt-dlp download** toggle is on by default; needed for HLS/DASH streams the multi-segment engine can't handle directly.
4. Click **Download Video** to start immediately, or **Queue** to add it without starting.

### If a Site Asks You to Log In

Some sites (or specific videos) only serve their video/audio to a logged-in session. If a probe or download fails for this reason, Kokoon shows two extra buttons alongside the error:

- **Log In in Browser** -- opens the video's site in your default desktop browser so you can sign in normally.
- **I've Logged In -- Retry** -- once you're signed in, click this to retry. Kokoon reads cookies from your default browser for that one retry, and if it succeeds, remembers that site so every future download from it works automatically -- no need to repeat this.

A couple of things worth knowing:
- This only works with browsers yt-dlp recognizes (Chrome, Edge, Firefox, Brave, Vivaldi, Opera).
- Some Chromium-based browsers lock their own cookie file while running. If retry fails with a cookie-database error, close that browser and try again.

### Automatic Updates

Kokoon checks for a newer `yt-dlp` in the background once per app launch and updates it silently if one's available -- video sites change how they serve content often, and this keeps grabbing working without you having to do anything. You'll see a brief tray notification when an update happens; no restart is needed.

---

## 8. Managing Downloads

### Pause and Resume

- Click the **pause icon** on an active download to pause it.
- Click the **play icon** on a paused download to resume.
- Use the **Pause All / Resume All** commands from the system tray (see [Section 10](#10-system-tray)).

### Cancel a Download

- Click the **X icon** on any download card to cancel it.
- Cancelled downloads appear in the Failed category.

### Open Completed Files

- Click the **open file icon** (appears on completed downloads) to open the file with its default application.
- Click the **folder icon** to open the containing folder in File Explorer.

### Filter Downloads

Use the header tabs to filter by status:
- **All Downloads** -- everything
- **Active** -- currently downloading
- **Queued** -- waiting in the queue
- **Completed** -- successfully finished
- **Failed** -- errors or cancelled

---

## 9. Settings

Open Settings from the sidebar. Available options:

### Downloads

| Setting | Description | Default |
|---------|-------------|---------|
| Default Save Path | Where files are saved | User's Downloads folder |
| Default Segment Count | Number of parallel connections (1-32) | 8 |
| Auto-start downloads | Begin downloading immediately when added | On |

### Speed

| Setting | Description | Default |
|---------|-------------|---------|
| Global Speed Limit | Maximum total download speed in MB/s (0 = unlimited) | 0 (unlimited) |

### Video Grabber

| Setting | Description | Default |
|---------|-------------|---------|
| Preferred Video Quality | Default quality when downloading videos | Best available |
| Allow yt-dlp full download | Falls back to yt-dlp for HLS/DASH streams | On |
| Auto-detect video URLs | Automatically probe URLs for video content in the Add Download dialog | On |

### Appearance

A single purple-accented theme with a three-way mode picker rather than a list of selectable themes:

| Setting | Description | Default |
|---------|-------------|---------|
| Theme mode | Dark / Light / System, selected via a 3-pill switch. System follows the Windows light/dark setting and updates live, including for already-open windows. | Dark |
| Show segment color visualization | Display colored segment bars on download cards | On |

---

## 10. System Tray

Kokoon runs in the system tray (notification area). Right-click the tray icon for options:

| Option | Action |
|--------|--------|
| Show Kokoon | Brings the main window to the front |
| Pause All | Pauses all active downloads |
| Resume All | Resumes all paused downloads |
| Exit | Shuts down Kokoon completely |

### Minimize to Tray

When "Minimize to Tray" is enabled in settings, closing the main window hides it to the tray instead of exiting. Use **Exit** from the tray menu to fully quit.

---

## 11. Troubleshooting

### Download Issues

**Download stuck at 0%**

- The server may not support range requests. Try reducing segments to 1.
- Check if the URL requires authentication (Kokoon downloads as an anonymous user).

**Download fails immediately**

- Verify the URL is accessible in your browser.
- Check if the file still exists on the server.
- Look at the logs in `%LOCALAPPDATA%\KokoonDownloader\logs\` for details.

**Video download fails**

- Ensure `yt-dlp.exe` and `ffmpeg.exe` are present in the `tools/` subfolder of the installation.
- Some sites may block downloads or require authentication.
- Try enabling "Use yt-dlp download" toggle in the Add Download dialog.

**Video says it needs a login**

- Use the **Log In in Browser** / **I've Logged In -- Retry** buttons that appear next to the error (see [Section 7](#7-downloading-videos)).
- If retry fails with a cookie-database error, close your default browser first -- some browsers lock their cookie file while running.

### App Issues

**App won't start**

- Ensure you're running Windows 10 1809+ or Windows 11.
- If using the portable version, try running as Administrator once.
- Check the log files in `%LOCALAPPDATA%\KokoonDownloader\logs\`.

**Database errors**

- Close Kokoon.
- Delete (or rename) `%LOCALAPPDATA%\KokoonDownloader\kokoon.db`.
- Restart Kokoon. A fresh database will be created. (This removes download history.)

---

## 12. Data Storage

Kokoon splits user data across two locations, following the usual Windows convention (local, machine-specific data vs. roaming preferences):

```
%LOCALAPPDATA%\KokoonDownloader\
  kokoon.db          <- SQLite database (download history, queue state)
  logs/
    kokoon-YYYYMMDD.log  <- Rolling daily log files

%APPDATA%\KokoonDownloader\
  settings.json      <- User preferences
```

Downloaded files go to your configured save path (default: your Downloads folder).

### Uninstalling Cleanly

If using the installer, the uninstaller removes the app files. To also remove user data:
1. Delete `%LOCALAPPDATA%\KokoonDownloader\` and `%APPDATA%\KokoonDownloader\`
