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

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO mi);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// Places the window like a Windows flyout: on the monitor under the pointer (the tray that was clicked),
    /// in the corner next to the taskbar, sized for that monitor's DPI. Works in physical pixels, before Show().
    /// True when the taskbar is at the top of the screen.
    public static bool PlaceNearTray(Window w, double widthDip, double heightDip)
    {
        GetCursorPos(out var p);
        var mon = MonitorFromPoint(p, 2 /* nearest */);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref mi)) return false;
        double s = GetDpiForMonitor(mon, 0, out var dpi, out _) == 0 ? dpi / 96.0 : 1;
        var work = mi.rcWork;
        int margin = (int)(12 * s);
        int wPx = (int)(widthDip * s);
        int hPx = Math.Min((int)(heightDip * s), work.Bottom - work.Top - 2 * margin);
        int x = work.Right - wPx - margin, y = work.Bottom - hPx - margin;
        if (work.Top > mi.rcMonitor.Top) y = work.Top + margin;    // taskbar at the top
        if (work.Left > mi.rcMonitor.Left) x = work.Left + margin; // taskbar on the left
        var h = new WindowInteropHelper(w).EnsureHandle();
        // Twice: a move onto a monitor with another DPI makes WPF rescale the window around its corner;
        // the second call, now at the right DPI, puts it exactly where it belongs.
        SetWindowPos(h, IntPtr.Zero, x, y, wPx, hPx, 0x0004 | 0x0010 /* no z-order change, no activate */);
        SetWindowPos(h, IntPtr.Zero, x, y, wPx, hPx, 0x0004 | 0x0010);
        return work.Top > mi.rcMonitor.Top;
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
