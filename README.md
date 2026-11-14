# Laneboard

Every project you're working on, at a glance: a native Windows 11 app that lives in the notification area.

- Finds every Git repository in the folders you choose (two levels deep)
- Shows the branch, uncommitted changes, commits to push or pull, the last commit, and the first lines of a `HANDOFF.md` note (with a SAFE POINT badge)
- Updates by itself a moment after a file changes in a repository: no refresh button needed
- Shows open pull requests and their checks, repository visibility, and self-hosted runners, all read through your own GitHub CLI (`gh`)
- Warns about a public repository with a self-hosted runner, and about GitHub Actions billing you money
- Notifies you (Windows notifications, can be turned off) when checks fail or pass, a new SAFE POINT is written,
  or a runner goes offline; a red dot on the tray icon while a project has a problem; projects that need you come first
- Pin projects to the top (right click > Pin to top); choose how often GitHub is read (2 to 30 minutes)
- Two views (Light rows or Advanced cards), System/Light/Dark themes, and a glass-to-solid background
- Keyboard first: type to filter at once, Down into the list, arrows between projects, Enter opens one (VS Code or the folder),
  the menu key or Shift+F10 opens its menu, F5 refreshes, Esc closes. Every button has a name a screen reader can say
- Opens like a Windows flyout, next to the taskbar on the monitor you clicked, sliding in unless Windows animations are off
- Read-only: it never writes to a repository (`git --no-optional-locks`), stores no token, and sends nothing anywhere except `gh` calls

Free to use, free to run: no account, no server, no telemetry. Progress: [ROADMAP.md](ROADMAP.md).

## Build

```bash
bash tools/check.sh      # tests + build/app/Laneboard.exe (single file, self-contained) + demo snapshots
powershell -NoProfile -ExecutionPolicy Bypass -File tools/live_check.ps1   # the real exe on a throwaway repo
```

Needs the .NET 10 SDK. The exe is also copied to `dist/` when no copy is running from there.
`python tools/make_icon.py` (Pillow) redraws the icon. Errors go to `%LOCALAPPDATA%\Laneboard\laneboard.log`.

`Laneboard.exe --snapshot out.png --demo [--theme light|dark] [--view light|advanced] [--solidity 0-100] [--settings]`
renders the window to a PNG without showing it.
