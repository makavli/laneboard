using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using WF = System.Windows.Forms;

namespace Laneboard;

/// The notification-area icon: left click toggles the flyout, right click has the menu.
/// A red dot on it when a project has a problem; Windows notifications for what just changed.
public sealed class Tray : IDisposable
{
    readonly WF.NotifyIcon _icon;
    readonly Icon _plain, _badged;
    readonly MainWindow _win;
    string? _target;

    public Tray(MainWindow win, Action quit)
    {
        _win = win;
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/laneboard.ico")).Stream;
        _plain = new Icon(stream, WF.SystemInformation.SmallIconSize);
        _badged = WithDot(_plain);
        _icon = new WF.NotifyIcon { Icon = _plain, Text = App.Name, Visible = true };
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add("Open " + App.Name, null, (_, _) => win.ShowFlyout());
        menu.Items.Add("Refresh", null, async (_, _) => await win.RefreshAsync(true));
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => quit());
        _icon.ContextMenuStrip = menu;
        _icon.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) win.ToggleFlyout(); };
        _icon.BalloonTipClicked += (_, _) => Open(_target);
        Toast.Init();
    }

    public void Show(Board board)
    {
        _icon.Text = board.TrayText.Length > 127 ? board.TrayText[..127] : board.TrayText;
        var want = board.Badge ? _badged : _plain;
        if (_icon.Icon != want) _icon.Icon = want;
    }

    /// A toast; a tray balloon only if toasts are unavailable (balloon limits: 63 / 255 characters).
    public void Notify(Alert a)
    {
        if (Toast.Show(a, () => _win.Dispatcher.BeginInvoke(() => Open(a.Target)))) return;
        _target = a.Target;
        _icon.ShowBalloonTip(10000, Cut(a.Title, 63), Cut(a.Body, 255), WF.ToolTipIcon.None);
    }

    void Open(string? target)
    {
        if (target == null) _win.ShowFlyout();
        else if (target.StartsWith("https://", StringComparison.Ordinal)) Shell.OpenUrl(target);
        else Shell.Open(target);
    }

    static string Cut(string s, int max) => s.Length > max ? s[..(max - 1)] + "…" : s;

    /// The app icon with a red dot in the lower-right corner, cut out from the icon by a thin transparent ring.
    public static Icon WithDot(Icon icon)
    {
        using var bmp = icon.ToBitmap();
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = bmp.Width * 0.5f, ring = Math.Max(1f, bmp.Width / 16f);
            var dot = new RectangleF(bmp.Width - d, bmp.Height - d, d, d);
            g.CompositingMode = CompositingMode.SourceCopy;
            using (var clear = new SolidBrush(Color.Transparent)) g.FillEllipse(clear, RectangleF.Inflate(dot, ring, ring));
            g.CompositingMode = CompositingMode.SourceOver;
            using var red = new SolidBrush(Color.FromArgb(0xC4, 0x2B, 0x1C)); // Windows 11 "critical"
            g.FillEllipse(red, dot);
        }
        var h = bmp.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(h);
            return (Icon)borrowed.Clone(); // the clone owns a copy of the handle
        }
        finally { DestroyIcon(h); }
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _plain.Dispose();
        _badged.Dispose();
    }
}
