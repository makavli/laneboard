# Live check of the real exe, on a throwaway repository (never the user's projects or settings).
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/live_check.ps1
# Shows the window (pinned) for ~15 s, beside any copy the user is running (test runs have their own instance lock).
# Writes build/app/live-before.png and build/app/live-after.png, prints memory use. Exit 0 = passed.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'build\app'
$exe = if ($env:LIVE_EXE) { $env:LIVE_EXE } else { Join-Path $out 'Laneboard.exe' }
if (-not (Test-Path $exe)) { Write-Output 'FAIL: build\app\Laneboard.exe missing (run tools/check.sh first)'; exit 1 }

Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System; using System.Drawing; using System.Runtime.InteropServices;
public static class Win {
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public struct RECT { public int L, T, R, B; }
  public static void Save(IntPtr h, string path) {
    RECT r; GetWindowRect(h, out r);
    using (var bmp = new Bitmap(r.R - r.L, r.B - r.T)) {
      using (var g = Graphics.FromImage(bmp)) { var dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); }
      bmp.Save(path);
    }
  }
}
"@

# A throwaway repository with one commit and a handoff note.
$repo = Join-Path $env:TEMP ("laneboard-live-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$proj = Join-Path $repo 'sample-project'
New-Item -ItemType Directory -Path $proj | Out-Null
Push-Location $proj
git init -q -b main
Set-Content -Path 'HANDOFF.md' -Value "# Handoff`n`nSAFE POINT: live check fixture.`n"
Set-Content -Path 'README.md' -Value 'fixture'  # tracked: HANDOFF*.md is often git-ignored globally
git add -A
git -c user.name=live -c user.email=live@example.invalid commit -q -m 'fixture'
Pop-Location

$log = Join-Path $env:LOCALAPPDATA 'Laneboard\laneboard.log'
$logLines = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }

# Laneboard's toasts in the notification centre (0 before the app ever registered its app id).
[void][Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
function ToastCount { try { @([Windows.UI.Notifications.ToastNotificationManager]::History.GetHistory('Laneboard')).Count } catch { 0 } }
$toastsBefore = ToastCount

$fail = @()
$p = Start-Process $exe -ArgumentList @('--root', $repo, '--pinned') -PassThru
try {
    $deadline = (Get-Date).AddSeconds(20)
    do { Start-Sleep -Milliseconds 300; $p.Refresh() } until ($p.MainWindowHandle -ne 0 -or (Get-Date) -gt $deadline -or $p.HasExited)
    if ($p.HasExited) { throw "the app exited (code $($p.ExitCode))" }
    if ($p.MainWindowHandle -eq 0) { throw 'no window within 20 s' }
    $h = $p.MainWindowHandle
    Start-Sleep -Seconds 4   # first read of the repository
    [Win]::Save($h, (Join-Path $out 'live-before.png'))

    # Placement: inside the work area of the monitor the window is on, hugging a corner.
    $r = New-Object Win+RECT; [void][Win]::GetWindowRect($h, [ref]$r)
    $screen = [System.Windows.Forms.Screen]::FromHandle($h).WorkingArea
    Write-Output ("window  L{0} T{1} R{2} B{3}" -f $r.L, $r.T, $r.R, $r.B)
    Write-Output ("work    L{0} T{1} R{2} B{3}" -f $screen.Left, $screen.Top, $screen.Right, $screen.Bottom)
    if ($r.L -lt $screen.Left -or $r.T -lt $screen.Top -or $r.R -gt $screen.Right -or $r.B -gt $screen.Bottom) { $fail += 'window outside the work area' }
    $gapR = $screen.Right - $r.R; $gapB = $screen.Bottom - $r.B
    if ($gapR -gt 64 -or $gapB -gt 64) { $fail += "not in the corner (gaps $gapR, $gapB px)" }

    # Instant update: a new file must show as "1 changed" within a few seconds, no refresh click.
    Set-Content -Path (Join-Path $proj 'new-file.txt') -Value 'x'
    Start-Sleep -Seconds 5
    [Win]::Save($h, (Join-Path $out 'live-after.png'))

    # Notification: a new safe point in the handoff adds a toast to the notification centre (kept there under Do not disturb too).
    Set-Content -Path (Join-Path $proj 'HANDOFF.md') -Value "# Handoff`n`nSAFE POINT: the live check wrote a new one.`n"
    Start-Sleep -Seconds 4
    $toasts = ToastCount
    Write-Output "toasts  $toastsBefore before, $toasts after"
    if ($toasts -le $toastsBefore) { $fail += 'no toast for the new safe point' }
    if ($p.HasExited) { $fail += 'the app exited during the check' }
    else {
        $p.Refresh()
        Write-Output ("memory  private {0:N0} MB, working set {1:N0} MB" -f ($p.PrivateMemorySize64 / 1MB), ($p.WorkingSet64 / 1MB))
    }
}
catch { $fail += $_.Exception.Message }
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    Start-Sleep -Milliseconds 500
    Remove-Item -Recurse -Force $repo -ErrorAction SilentlyContinue
}

if (Test-Path $log) {
    $new = @(Get-Content $log | Select-Object -Skip $logLines)
    if ($new.Count) { $fail += "errors logged during the run: $($new[0])" }
}

if ($fail.Count) { Write-Output ("LIVE CHECK FAILED: " + ($fail -join '; ')); exit 1 }
Write-Output 'LIVE CHECK PASSED (look at build/app/live-before.png and live-after.png: "clean" then "1 changed"; a "Safe point" toast in the notification centre)'
