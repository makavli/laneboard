using System.IO;
using System.Text.Json;

namespace Laneboard;

/// Which of the user's repositories were public on each day Laneboard ran. GitHub's API only tells today's visibility,
/// but its free Actions allowance counts each run by the visibility the repository had then.
public sealed class VisibilityLog
{
    const int KeepDays = 70; // the whole month before too, so the 1st of a month has a day before it
    readonly SortedDictionary<string, string[]> _days; // "yyyy-MM-dd" -> public repository names
    bool _dirty;

    public VisibilityLog(IDictionary<string, string[]>? days = null) => _days = new(days ?? new Dictionary<string, string[]>(), StringComparer.Ordinal);

    public static string FilePath => Path.Combine(Settings.Dir, "visibility.json");

    public static VisibilityLog Load()
    {
        try { return new(JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(FilePath))); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        if (!_dirty) return;
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_days));
            _dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("visibility log", ex); }
    }

    // shortcut: the last reading of a day stands for the whole day; a repository switched mid-day is counted one way all day.
    public void Record(string day, IEnumerable<string> publicRepos)
    {
        var names = publicRepos.Select(n => n.ToLowerInvariant()).Distinct().Order(StringComparer.Ordinal).ToArray();
        if (_days.TryGetValue(day, out var old) && old.SequenceEqual(names)) return;
        _days[day] = names;
        while (_days.Count > KeepDays) _days.Remove(_days.Keys.First());
        _dirty = true;
    }

    /// The latest reading on or before that day (a day Laneboard didn't run keeps the reading before it); null before the first.
    public IReadOnlySet<string>? PublicOn(string day)
    {
        string[]? last = null;
        foreach (var (k, v) in _days)
        {
            if (string.CompareOrdinal(k, day) > 0) break;
            last = v;
        }
        return last?.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
