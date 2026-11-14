using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Laneboard;

/// Windows 11 materials: acrylic for the flyout, Mica for the pinned window, the dark title frame, round corners.
static class Backdrop
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    const int DarkMode = 20, CornerPreference = 33, SystemBackdrop = 38, GwlStyle = -16, WsSysMenu = 0x80000;

    static bool Supported => Environment.OSVersion.Version.Build >= 22621;

    public static bool SystemIsDark()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    public static bool IsDark(string theme) => theme == "Dark" || (theme != "Light" && SystemIsDark());

    public static void Apply(Window w, bool pinned, string theme)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        // With the frame extended over the whole window, DWM would draw its own caption buttons over ours.
        SetWindowLong(h, GwlStyle, GetWindowLong(h, GwlStyle) & ~WsSysMenu);
        int dark = IsDark(theme) ? 1 : 0;
        DwmSetWindowAttribute(h, DarkMode, ref dark, sizeof(int));
        int round = 2;
        DwmSetWindowAttribute(h, CornerPreference, ref round, sizeof(int));
        if (Supported)
        {
            HwndSource.FromHwnd(h)!.CompositionTarget!.BackgroundColor = Colors.Transparent;
            w.Background = Brushes.Transparent;
            int type = pinned ? 2 : 3; // 2 = Mica (a window), 3 = acrylic (a flyout)
            DwmSetWindowAttribute(h, SystemBackdrop, ref type, sizeof(int));
        }
        else w.SetResourceReference(Window.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
    }
}
