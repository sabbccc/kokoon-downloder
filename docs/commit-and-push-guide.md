# Commit & Push Guide

One-time GitHub setup plus the everyday git workflow for this repo.

## 1. One-time: create the GitHub repo

Do this on [github.com/new](https://github.com/new) — name it, set visibility, and **don't** initialize with a README/license/gitignore (you already have all three locally). Copy the repo URL it gives you (SSH or HTTPS).

## 2. Review what you're about to commit

```powershell
git status
```

> **Check before staging**: confirm nothing sensitive is in the list — API keys, connection strings, personal file paths. `bin/`, `obj/`, `publish/`, `*.log`, `*.db`, and the bundled `yt-dlp.exe`/`ffmpeg.exe` are already excluded via `.gitignore`.

## 3. Stage and make the initial commit

```powershell
git add .
git status   # double-check the staged list looks right
git commit -m "Initial commit"
```

## 4. Connect the remote and push

```powershell
# replace with your actual repo URL
git remote add origin https://github.com/<you>/<repo>.git
git branch -M main
git push -u origin main
```

After this first push, `git push` alone works for every commit after — the `-u` set the upstream tracking branch.

## ✓ Everyday workflow, after setup

```powershell
git status
git add <specific files>   # prefer this over `git add .` once history exists
git commit -m "Short, why-focused message"
git push
```

## Good habits going forward

- Stage specific files by name rather than `git add -A`/`git add .` once the repo has history — an unnoticed stray file is much easier to catch that way.
- Write commit messages around *why*, not a restatement of the diff.
- Never force-push (`git push --force`) to `main` unless you specifically mean to overwrite remote history — ask first if unsure.
- If a file looks like it should be tracked but isn't showing up in `git status`, check `.gitignore` first.

---
Kokoon Downloader · internal git reference
