using System.Diagnostics;
using System.IO;

namespace Laneboard;

/// Opening things the way Windows does: Explorer, the default browser, Windows Terminal, VS Code.
public static class Shell
{
    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException) { }
    }

    /// Only web links GitHub handed us, never anything else.
    public static void OpenUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps) Open(u.AbsoluteUri);
    }

    public static void Terminal(string dir)
    {
        var wt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        var psi = File.Exists(wt)
            ? new ProcessStartInfo(wt) { ArgumentList = { "-d", dir }, UseShellExecute = false }
            : new ProcessStartInfo("powershell.exe") { ArgumentList = { "-NoExit" }, WorkingDirectory = dir, UseShellExecute = true };
        try { Process.Start(psi)?.Dispose(); } catch (System.ComponentModel.Win32Exception) { }
    }

    static readonly Lazy<string?> CodePath = new(() =>
    {
        // code.cmd sits in <VS Code>\bin; Code.exe one folder up. Starting Code.exe avoids a console window.
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (!File.Exists(Path.Combine(dir, "code.cmd"))) continue;
                var exe = Path.GetFullPath(Path.Combine(dir, "..", "Code.exe"));
                if (File.Exists(exe)) return exe;
            }
            catch (ArgumentException) { }
        }
        return null;
    });

    public static bool HasCode => CodePath.Value != null;

    public static void Code(string dir)
    {
        if (CodePath.Value is not { } exe) return;
        try { Process.Start(new ProcessStartInfo(exe) { ArgumentList = { dir }, UseShellExecute = false })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
