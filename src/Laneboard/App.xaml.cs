using System.Threading;
using System.Windows;

namespace Laneboard;

public partial class App : Application
{
    public const string Name = "Laneboard";

    Mutex? _single;
    Tray? _tray;
    MainWindow? _win;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        bool demo = args.Contains("--demo");

        if (Arg(args, "--snapshot") is { } png)
        {
            int code = await Snapshot.Run(png, demo, Arg(args, "--theme"), args.Contains("--settings"),
                Arg(args, "--view"), int.TryParse(Arg(args, "--solidity"), out var so) ? so : null);
            Shutdown(code);
            return;
        }

        // One instance per user: a second launch just brings the first one forward.
        _single = new Mutex(true, $"{Name}.Single.{Environment.UserName}", out bool first);
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, $"{Name}.Show.{Environment.UserName}");
        if (!first)
        {
            AllowSetForegroundWindow(-1); // we were just launched by the user: lend the running copy the right to come forward
            show.Set();
            Shutdown();
            return;
        }

        _win = new MainWindow(Settings.Load(), demo);
        _tray = new Tray(_win, Quit);
        _win.SummaryChanged += s => _tray.SetText(s);
        new Thread(() => { while (show.WaitOne()) Dispatcher.Invoke(_win.ShowFlyout); }) { IsBackground = true }.Start();

        if (!args.Contains("--tray")) _win.ShowFlyout();
        await _win.RefreshAsync(true);
    }

    void Quit()
    {
        _tray?.Dispose();
        _win?.AllowClose();
        Shutdown();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);

    static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
