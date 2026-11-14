using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Laneboard;

public sealed class Settings
{
    public List<string> Roots { get; set; } = [];
    public List<string> Hidden { get; set; } = [];
    public bool Pinned { get; set; }
    public string Theme { get; set; } = "System";   // System | Light | Dark
    public string View { get; set; } = "Light";     // Light | Advanced
    public int Solidity { get; set; }               // 0 = glass, 100 = solid background

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), App.Name);
    static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        if (!File.Exists(FilePath)) return new Settings { Roots = GuessRoots() };
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Keep the broken file for the user instead of overwriting it on the next save.
            try { File.Copy(FilePath, FilePath + ".broken", true); } catch (IOException) { }
            return new();
        }
    }

    public void Save()
    {
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

    // shortcut: the Run key works for the plain .exe; a Store (MSIX) build needs a StartupTask instead.
    public static bool StartWithWindows
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(App.Name) is string;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue(App.Name, $"\"{Environment.ProcessPath}\" --tray");
            else k.DeleteValue(App.Name, false);
        }
    }
}
