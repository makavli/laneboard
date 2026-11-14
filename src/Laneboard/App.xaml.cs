using System.Threading;
using System.Windows;

namespace Laneboard;

public partial class App : Application
{
    public const string Name = "Laneboard";

    /// True inside the Store (MSIX) package: start-with-Windows and notifications then go through the package identity.
    public static readonly bool Packaged = IsPackaged();

    Mutex? _single;
    Tray? _tray;
    MainWindow? _win;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // A tray app must not vanish on one bad refresh: log the error and keep running.
        DispatcherUnhandledException += (_, ex) => { Log.Error("ui", ex.Exception); ex.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log.Error("task", ex.Exception); ex.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => { if (ex.ExceptionObject is Exception x) Log.Error("fatal", x); };
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
        // A test run (--root) gets its own lock, so it can run beside the user's copy without touching it.
        var lane = $"{Environment.UserName}{(Arg(args, "--root") != null ? ".test" : "")}";
        _single = new Mutex(true, $"{Name}.Single.{lane}", out bool first);
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, $"{Name}.Show.{lane}");
        if (!first)
        {
            AllowSetForegroundWindow(-1); // we were just launched by the user: lend the running copy the right to come forward
            show.Set();
            Shutdown();
            return;
        }

        var settings = Settings.Load();
        if (Arg(args, "--root") is { } root) { settings.Roots = [root]; settings.Hidden = []; settings.ReadOnly = true; }
        if (args.Contains("--pinned")) { settings.Pinned = true; settings.ReadOnly = true; }
        _win = new MainWindow(settings, demo);
        _tray = new Tray(_win, Quit);
        _win.BoardChanged += (board, alert) =>
        {
            _tray.Show(board);
            if (alert != null) _tray.Notify(alert);
        };
        new Thread(() => { while (show.WaitOne()) Dispatcher.Invoke(_win.ShowFlyout); }) { IsBackground = true }.Start();

        if (!args.Contains("--tray") && !StartedAtSignIn()) _win.ShowFlyout();
        await _win.RefreshAsync(true);
    }

    void Quit()
    {
        _tray?.Dispose();
        _win?.AllowClose();
        Shutdown();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern int GetCurrentPackageFullName(ref int length, IntPtr name);
    static bool IsPackaged() { int n = 0; return GetCurrentPackageFullName(ref n, IntPtr.Zero) != 15700; } // APPMODEL_ERROR_NO_PACKAGE

    /// The package's startup task passes no arguments: Windows says how the app was started instead.
    static bool StartedAtSignIn()
    {
        if (!Packaged) return false;
        try { return global::Windows.ApplicationModel.AppInstance.GetActivatedEventArgs()?.Kind == global::Windows.ApplicationModel.Activation.ActivationKind.StartupTask; }
        catch (Exception ex) { Log.Error("activation", ex); return false; }
    }

    static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
