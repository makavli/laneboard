# Laneboard privacy policy

Laneboard collects no personal data and sends nothing to its makers. It has no account, no analytics, no
telemetry and no advertising.

## What it reads on your PC
- The folders you add in its settings: it lists the Git repositories inside them and reads each one's branch,
  changed files, last commit and handoff note (`HANDOFF.md`). It only reads them; it never changes your files.
- Whether a GitHub Actions runner is running on this PC.

## What it reads from GitHub
If the GitHub CLI (`gh`) is installed and signed in, Laneboard runs it to read, for your own repositories:
pull requests and their checks, repository visibility, self-hosted runners, and this month's GitHub Actions
usage. These requests go from your PC to GitHub under your own `gh` sign-in. Laneboard stores no token and talks
to no other server. Without `gh`, Laneboard works offline.

## What it stores
Its settings and an error log, in its own folder on this PC (`%LOCALAPPDATA%\Laneboard`, or the package's private
folder when installed from the Microsoft Store). Uninstalling the Store app removes them.

## Notifications
Windows notifications (which you can turn off in its settings) are shown by Windows on this PC only.

## Contact
Questions: open an issue on the project's GitHub repository.
