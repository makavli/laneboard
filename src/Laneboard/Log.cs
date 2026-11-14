using System.IO;

namespace Laneboard;

/// %LOCALAPPDATA%\Laneboard\laneboard.log: what went wrong, so a bug report can say more than "it stopped".
public static class Log
{
    static readonly object Gate = new();
    const long MaxBytes = 1_000_000;

    public static string FilePath => Path.Combine(Settings.Dir, "laneboard.log");

    public static void Error(string where, Exception ex) => Write($"ERROR {where}: {ex}");

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Settings.Dir);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes) File.Move(FilePath, FilePath + ".old", true); // keep one old file
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
        }
        catch (IOException) { /* logging must never take the app down */ }
        catch (UnauthorizedAccessException) { }
    }
}
