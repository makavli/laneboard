using System.IO;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Laneboard;

/// Windows toast notifications. Unlike tray balloons, which Windows drops while Do not disturb is on,
/// toasts wait in the notification centre.
static class Toast
{
    static ToastNotifier? _notifier;

    /// An unpackaged app needs an app id Windows knows: registered for this user (no Start menu shortcut needed),
    /// with the name and icon its notifications show. On failure the tray falls back to balloons.
    public static void Init()
    {
        try
        {
            if (App.Packaged) { _notifier = ToastNotificationManager.CreateToastNotifier(); return; } // name and icon come from the package
            var icon = Path.Combine(Settings.Dir, "laneboard.png");
            if (!File.Exists(icon))
            {
                Directory.CreateDirectory(Settings.Dir);
                var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/laneboard.ico")).Stream;
                using var ico = new System.Drawing.Icon(stream, 256, 256);
                using var bmp = ico.ToBitmap();
                bmp.Save(icon, System.Drawing.Imaging.ImageFormat.Png);
            }
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{App.Name}"))
            {
                k.SetValue("DisplayName", App.Name);
                k.SetValue("IconUri", icon);
            }
            _notifier = ToastNotificationManager.CreateToastNotifier(App.Name);
        }
        catch (Exception ex) { Log.Error("toast", ex); }
    }

    /// The title and the body's lines, three lines at most (Windows' limit). `clicked` runs on a background thread.
    // shortcut: a click works while Laneboard runs; after Quit, a toast left in the centre opens nothing (needs a COM activator).
    public static bool Show(Alert a, Action clicked)
    {
        if (_notifier == null) return false;
        try
        {
            var xml = new XmlDocument();
            xml.LoadXml("<toast><visual><binding template=\"ToastGeneric\"/></visual></toast>");
            var binding = xml.GetElementsByTagName("binding")[0];
            foreach (var line in a.Body.Split('\n').Prepend(a.Title).Take(3))
            {
                var text = xml.CreateElement("text");
                text.AppendChild(xml.CreateTextNode(line)); // a text node: no XML escaping to get wrong
                binding.AppendChild(text);
            }
            var toast = new ToastNotification(xml);
            toast.Activated += (_, _) => clicked();
            _notifier.Show(toast);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("toast", ex);
            return false;
        }
    }
}
