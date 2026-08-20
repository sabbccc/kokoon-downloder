# Publish & Portable Bundle

Kokoon Downloader — self-contained Release publish, portable zip, and Desktop test copy.

> **Before you start**: run every command from the repo root (`F:\AI\KokoonDownloader`). Close any running `Kokoon.UI.exe` first — the Desktop-copy step will fail on locked files otherwise.

## 1. Self-contained Release publish

```powershell
$root      = "F:\AI\KokoonDownloader"
$appxTools = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Microsoft\VisualStudio\v18.0\AppxPackage/"
$publish   = "$root\publish"

Remove-Item "$publish\app" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$root\Kokoon.UI\Kokoon.UI.csproj" `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -p:WindowsPackageType=None `
  -p:AppxMSBuildToolsPath=$appxTools `
  -o "$publish\app"
```

## 2. Assemble the portable folder

```powershell
Remove-Item "$publish\KDM-Portable" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item "$publish\app" "$publish\KDM-Portable" -Recurse
```

## 3. Zip it

```powershell
Remove-Item "$publish\KDM-Portable-win-x64.zip" -Force -ErrorAction SilentlyContinue
Compress-Archive -Path "$publish\KDM-Portable\*" -DestinationPath "$publish\KDM-Portable-win-x64.zip" -Force
```

## 4. Push to Desktop test copy (optional)

```powershell
Remove-Item "$env:USERPROFILE\Desktop\KDM-Portable" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item "$publish\KDM-Portable" "$env:USERPROFILE\Desktop\KDM-Portable" -Recurse
```

## ✓ Verify

```powershell
Get-Item "$publish\KDM-Portable-win-x64.zip" | Select-Object Name, @{N='SizeMB';E={[math]::Round($_.Length/1MB,1)}}
```

## Notes

- `AppxMSBuildToolsPath` must end in `/` not `\` — a trailing backslash gets swallowed by MSBuild's property-escaping and breaks the path.
- Step 1 requires Visual Studio's "Windows application development" workload (for the Appx build tools) — adjust the path if your VS version/edition/install location differs.
- Bundled `yt-dlp.exe` / `ffmpeg.exe` come from `Kokoon.VideoGrabber\tools\` automatically via `CopyToOutputDirectory` — no separate copy step needed.
- Step 4 fails to overwrite locked files if the app is currently running from that folder — close it first.

---
Kokoon Downloader · internal packaging reference
