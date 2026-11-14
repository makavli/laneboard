# The README GIF: the real exe in demo mode (made-up projects, never the user's), driven by UI Automation and filmed
# from its own window (PrintWindow), so nothing else on the screen gets in. Runs beside the user's copy (own lock).
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/make_gif.ps1     Output: docs/laneboard.gif
# Needs ffmpeg on PATH. Shows the demo window for ~20 s.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'build\app\Laneboard.exe'
$frames = Join-Path $root 'build\gif'
$gif = Join-Path $root 'docs\laneboard.gif'
if (-not (Test-Path $exe)) { Write-Output 'FAIL: build\app\Laneboard.exe missing (run tools/check.sh first)'; exit 1 }
Remove-Item -Recurse -Force $frames -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $frames, (Split-Path $gif) | Out-Null

Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System; using System.Drawing; using System.Runtime.InteropServices;
public static class Film {
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  public struct RECT { public int L, T, R, B; }
  public static int Count;
  public static void Frame(IntPtr h, string dir) {
    RECT r; GetWindowRect(h, out r);
    using (var bmp = new Bitmap(r.R - r.L, r.B - r.T)) {
      using (var g = Graphics.FromImage(bmp)) { var dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); }
      bmp.Save(String.Format("{0}\\f{1:D4}.png", dir, Count++));
    }
  }
  // Hover without touching the user's mouse: a mouse-move message in client coordinates.
  public static void Hover(IntPtr h, int x, int y) {
    RECT r; GetWindowRect(h, out r);
    PostMessage(h, 0x0200, IntPtr.Zero, (IntPtr)(((y - r.T) << 16) | ((x - r.L) & 0xFFFF)));
  }
}
"@

$fps = 12
function Hold([double]$seconds) {
    $n = [int]($seconds * $fps)
    for ($i = 0; $i -lt $n; $i++) {
        $t = [Diagnostics.Stopwatch]::StartNew()
        [Film]::Frame($script:h, $frames)
        $left = [int](1000 / $fps) - $t.ElapsedMilliseconds
        if ($left -gt 0) { Start-Sleep -Milliseconds $left }
    }
}
function Named([string]$name) {
    $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $e = $script:uia.FindFirst('Descendants', $c)
    if (-not $e) { throw "no element named '$name'" }
    $e
}
function Press([string]$name) { (Named $name).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function HoverItem([int]$index) {
    $items = @($script:uia.FindAll('Descendants', (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))))
    $b = $items[$index].Current.BoundingRectangle
    [Film]::Hover($script:h, [int]($b.X + $b.Width * 0.6), [int]($b.Y + $b.Height / 2))
}

$p = Start-Process $exe -ArgumentList @('--demo', '--pinned', '--theme', 'light', '--view', 'light') -PassThru
try {
    $deadline = (Get-Date).AddSeconds(20)
    do { Start-Sleep -Milliseconds 300; $p.Refresh() } until ($p.MainWindowHandle -ne 0 -or (Get-Date) -gt $deadline -or $p.HasExited)
    if ($p.MainWindowHandle -eq 0) { throw 'no window within 20 s' }
    $script:h = $p.MainWindowHandle
    $script:uia = [System.Windows.Automation.AutomationElement]::FromHandle($h)
    Start-Sleep -Seconds 2   # demo data and fonts settle

    Hold 1.6                                  # the light view: every project, one row each
    Press 'Advanced view'; Hold 2.6           # full cards: pull requests, checks, handoff notes
    Press 'Settings'; Hold 1.2
    (Named 'Dark').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Hold 1.4
    Press 'Back'; Hold 2.2                    # the same cards in the dark theme
    Press 'Light view'; Hold 2.0
}
finally { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }

# One palette for the whole film, only changed rectangles redrawn: small and sharp.
$pattern = Join-Path $frames 'f%04d.png'
ffmpeg -v error -y -framerate $fps -i $pattern -vf "split[a][b];[a]palettegen=stats_mode=diff:max_colors=192[p];[b][p]paletteuse=dither=sierra2_4a:diff_mode=rectangle" -loop 0 $gif
if ($LASTEXITCODE -ne 0) { Write-Output 'FAIL: ffmpeg'; exit 1 }
Write-Output ("GIF {0} frames, {1:N1} MB -> docs/laneboard.gif" -f [Film]::Count, ((Get-Item $gif).Length / 1MB))
