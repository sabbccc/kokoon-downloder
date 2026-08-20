# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Kokoon Downloader — a native Windows desktop download manager (IDM/ADM-style), built with WinUI 3 on .NET 8. Multi-segment parallel HTTP downloading with resume support, plus a bundled yt-dlp/ffmpeg video-grabber path for video URLs. `IDEA.md` and `docs/00_MASTER_BRIEF.md` describe the original intent.

## Solution structure

`KokoonDownloader.slnx` (the modern XML solution format, not `.sln`) references exactly three projects:

- **Kokoon.Core** — plain .NET 8 class library, zero UI dependency. HTTP download engine (`Engine/`), in-memory queue + scheduler (`Queue/`), EF Core/SQLite persistence (`Persistence/`), shared models (`Models/`).
- **Kokoon.VideoGrabber** — plain .NET 8 class library wrapping `yt-dlp.exe`/`ffmpeg.exe` (bundled under `Kokoon.VideoGrabber/tools/`) for URLs the segment engine can't handle (HLS/DASH streams, video sites).
- **Kokoon.UI** — the WinUI 3 app (`net8.0-windows10.0.19041.0`, `WindowsPackageType=None` — unpackaged, no MSIX). References both of the above. Only this project builds for `x86`/`x64`/`arm64`; Core and VideoGrabber are plain AnyCPU libraries.

There is no browser extension / native-messaging-bridge component — that was part of the original scaffold spec (still described historically in a few `docs/` files) but was dropped; this is a standalone downloader with no browser integration.

## Build

Requires .NET 8 SDK (`global.json` pins `8.0.0`, roll-forward `latestMinor`) and Visual Studio with the "Windows application development" workload (needed for the WindowsAppSDK/Appx build tools that `Kokoon.UI` depends on even though `WindowsPackageType=None`).

```powershell
dotnet build KokoonDownloader.slnx
```

Kokoon.UI specifically has historically needed to be built via MSBuild with an explicit platform (see `.claude/settings.local.json` for the exact paths used on this machine):

```powershell
dotnet build Kokoon.UI/Kokoon.UI.csproj -p:Platform=x64 -p:Configuration=Debug
```

Publishing (self-contained, per-project, not via the solution) is documented in detail in `docs/DEVELOPER_PACKAGING_GUIDE.md` — read it before changing publish profiles or `.csproj` publish targets; it explains why the UI project can't use `PublishSingleFile` and why `AppxMSBuildToolsPath` is required.

There is no test project in this repo (`docs/HOW_TO_USE.md` references `tests/Kokoon.Core.Tests` and `tests/Kokoon.Integration.Tests` from the original scaffold, but they were never added).

## Architecture

**Data flow**: `App.xaml.cs` builds a single `ServiceCollection` (no generic host) at construction time and stores it as `App.Services` (static). `OnLaunched` loads `ISettingsService`, runs EF Core migrations via `IDbContextFactory<KokoonDbContext>`, creates `MainWindow`, then wires `DownloadProgressBridge` between `DownloadScheduler` (Kokoon.Core) and `MainViewModel` (Kokoon.UI), marshaling progress callbacks onto the UI thread via `DispatcherQueue.TryEnqueue`. ViewModels never touch the engine directly — only through the scheduler/bridge.

**Download engine** (`Kokoon.Core/Engine/DownloadEngine.cs`): probes the URL with `HEAD` (falls back to ranged `GET`) to get size + `Accept-Ranges` support, splits into up to 32 segments (min 1MB/segment, default 8) downloaded in parallel via `SegmentDownloader`, then `SegmentAssembler` writes them into the final file at their byte offsets (no post-hoc concatenation). Falls back to single-stream download when the server doesn't support ranges. Segment byte-ranges persist to SQLite so resume only re-requests missing ranges.

**Video path**: `IExternalDownloader` (implemented by `YtDlpFullDownloadHandler` in Kokoon.VideoGrabber) is the escape hatch for URLs the segment engine can't handle — routed through `YtDlpManager`/`YtDlpProbe`/`YtDlpUrlExtractor`/`YtDlpDownloader`, muxed with `FfmpegMuxer`.

**Persistence**: EF Core + SQLite, DB at `%LOCALAPPDATA%\KokoonDownloader\kokoon.db`, migrated automatically on launch (`Persistence/Migrations/`). `DownloadItemEntity` is the EF-mapped shape; `DownloadItem` (Models/) is the domain model used by the engine/UI — check `DownloadRepository` when changing either, since it maps between them.

**Settings**: `ISettingsService` (Kokoon.UI/Settings) is a JSON file at `%APPDATA%\KokoonDownloader\settings.json`, single-writer-locked, defaults applied for any missing property on load.

**Theming**: a single Neon theme pack (`AppSettings.Theme`, internal identifier `"NeonDark"` — a pre-existing name, not user-facing) with a **Dark/Light/System mode** picker (`AppSettings.BaseTheme`, `BaseThemeMode`). The Settings page only exposes the mode `RadioButtons` (`Kokoon.UI/Views/SettingsPage.xaml`) — there is no theme-pack picker; `SettingsPage.xaml.cs` always passes its `ThemeName` constant to `App.ApplyTheme`, which is now vestigial (kept only for call-site compatibility).

`Kokoon.UI/Themes/NeonDark.xaml` is a single file holding both Dark and Light brush sets via `<ResourceDictionary.ThemeDictionaries>` (`x:Key="Dark"`/`"Light"`), plus `GenericStyles.xaml` merged alongside it. All brush-valued resources are referenced via `{ThemeResource ...}` (not `{StaticResource}`) throughout `Kokoon.UI/Views/*.xaml` and inside the Style Setters in `NeonDark.xaml` itself — this is what makes Dark/Light/System switch **live**, including for already-open windows/dialogs, without an app restart. `App.ApplyTheme(themeName, mode)` (`App.xaml.cs`) just sets `MainWindow.Content`'s `RequestedTheme` (`Dark`/`Light`/`Default` for System); WinUI re-resolves every `ThemeResource` reference automatically from there. `App.xaml` deliberately does **not** set `Application.RequestedTheme` — leaving it unset lets `ElementTheme.Default` (System mode) genuinely follow the OS setting instead of a hardcoded fallback. Non-brush resources (`CornerRadius*`, `Thickness*`) and all Styles (`NeonButtonStyle`, `SidebarButtonStyle`, etc.) live at the top level of the same file, outside `ThemeDictionaries`, since they don't vary by mode — only the brushes their Setters point to do.

(Earlier revisions of this app supported multiple selectable theme packs — Argentina/Bangladesh/Barcelona plus a separate "Neon Light" pack — each swappable independently of the Dark/Light/System mode. That was removed; Neon is now the only pack.)

## Conventions carried over from the original spec (`docs/00_MASTER_BRIEF.md`)

- Async methods suffixed `Async`; `CancellationToken` threaded through all async call chains; no `async void` except event handlers.
- `HttpClient` only (never `WebClient`), `Task`/`async` only (never raw `Thread`), never `.Result`/`.Wait()`.
- No business logic in ViewModels — they orchestrate, the engine/repository/scheduler own logic.
- `Kokoon.Core` has zero UI dependency and XML doc comments on public APIs (`GenerateDocumentationFile` is on in its `.csproj`).
