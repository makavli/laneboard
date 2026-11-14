# Laneboard (working name)

Every project you're working on, at a glance: a native Windows 11 app that lives in the notification area.

- Finds every Git repository in the folders you choose (two levels deep)
- Shows the branch, uncommitted changes, commits to push or pull, the last commit, and the first lines of a `HANDOFF.md` note (with a SAFE POINT badge)
- Shows open pull requests and their checks, repository visibility, and self-hosted runners, all read through your own GitHub CLI (`gh`)
- Warns about a public repository with a self-hosted runner, and about GitHub Actions billing you money
- Two views (Light rows or Advanced cards), System/Light/Dark themes, and a glass-to-solid background
- Read-only: it never writes to a repository (`git --no-optional-locks`), stores no token, and sends nothing anywhere except `gh` calls

## Build

```bash
bash tools/check.sh      # tests + dist/Laneboard.exe (single file, self-contained) + demo snapshots
```

Needs the .NET 10 SDK. `python tools/make_icon.py` (Pillow) redraws the icon.

`Laneboard.exe --snapshot out.png --demo [--theme light|dark] [--view light|advanced] [--solidity 0-100] [--settings]`
renders the window to a PNG without showing it.
