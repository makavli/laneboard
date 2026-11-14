using Windows.ApplicationModel;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Laneboard;

public sealed class Settings
{
    public List<string> Roots { get; set; } = [];
    public List<string> Hidden { get; set; } = [];
    public List<string> PinnedProjects { get; set; } = []; // repository paths kept at the top
    public int GitHubMinutes { get; set; } = 5;             // how often GitHub is read (2 min while checks run)
    public bool Pinned { get; set; }
    public string Theme { get; set; } = "System";   // System | Light | Dark
    public string View { get; set; } = "Light";     // Light | Advanced
    public int Solidity { get; set; }               // 0 = glass, 100 = solid background
    public bool Notify { get; set; } = true;        // Windows notifications for failing/passing checks, safe points, runners

    /// Set for test runs (`--root`, `--pinned`): the in-memory overrides must never reach the user's file.
    [System.Text.Json.Serialization.JsonIgnore] public bool ReadOnly { get; set; }

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), App.Name);
    static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        if (!File.Exists(FilePath)) return new Settings { Roots = GuessRoots() };
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
            s.Roots ??= []; s.Hidden ??= []; s.PinnedProjects ??= []; // "Roots": null in a hand-edited file
            return s;
        }
        catch (UnauthorizedAccessException) { return new(); }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Keep the broken file for the user instead of overwriting it on the next save.
            try { File.Copy(FilePath, FilePath + ".broken", true); } catch (IOException) { }
            return new();
        }
    }

    public void Save()
    {
        if (ReadOnly) return;
        Directory.CreateDirectory(Dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, true);
    }

    /// First run: the folders where Windows developers usually keep their code, if they hold any repository.
    static List<string> GuessRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string[] candidates =
        [
            Path.Combine(home, "source", "repos"), Path.Combine(home, "repos"), Path.Combine(home, "Projects"),
            Path.Combine(home, "dev"), Path.Combine(home, "code"), Path.Combine(home, "src"),
            Path.Combine(docs, "GitHub"), Path.Combine(home, "GitHub"),
        ];
        return candidates.Where(c => Directory.Exists(c) && Scanner.Discover([c], []).Count > 0).ToList();
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string StartupTaskId = "LaneboardStartup"; // = TaskId in store/AppxManifest.xml

    /// The plain .exe uses the Run key; the Store package can't (its registry is private), so it uses its startup task,
    /// which the user can also turn off in Windows Settings > Apps > Startup.
    public static bool StartWithWindows
    {
        get
        {
            if (App.Packaged)
                try
                {
                    return Task.Run(async () => (await StartupTask.GetAsync(StartupTaskId)).State
                        is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy).GetAwaiter().GetResult();
                }
                catch (Exception ex) { Log.Error("startup task", ex); return false; }
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(App.Name) is string;
        }
        set
        {
            if (App.Packaged)
            {
                // Windows refuses to turn it back on if the user turned it off in Windows Settings: the box then shows that.
                try
                {
                    Task.Run(async () =>
                    {
                        var t = await StartupTask.GetAsync(StartupTaskId);
                        if (value) await t.RequestEnableAsync(); else t.Disable();
                    }).GetAwaiter().GetResult();
                }
                catch (Exception ex) { Log.Error("startup task", ex); }
                return;
            }
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue(App.Name, $"\"{Environment.ProcessPath}\" --tray");
            else k.DeleteValue(App.Name, false);
        }
    }
}
