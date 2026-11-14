using System.Windows;
using WF = System.Windows.Forms;

namespace Laneboard;

/// The notification-area icon: left click toggles the flyout, right click has the menu.
public sealed class Tray : IDisposable
{
    readonly WF.NotifyIcon _icon;

    public Tray(MainWindow win, Action quit)
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/laneboard.ico")).Stream;
        _icon = new WF.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, WF.SystemInformation.SmallIconSize),
            Text = App.Name,
            Visible = true,
        };
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add("Open " + App.Name, null, (_, _) => win.ShowFlyout());
        menu.Items.Add("Refresh", null, async (_, _) => await win.RefreshAsync(true));
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => quit());
        _icon.ContextMenuStrip = menu;
        _icon.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) win.ToggleFlyout(); };
    }

    public void SetText(string s) => _icon.Text = s.Length > 127 ? s[..127] : s;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
