using System.IO;

namespace Laneboard;

/// Watches every repository's files and remembers which ones changed, so only those are re-read.
public sealed class RepoWatcher : IDisposable
{
    readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _dirty = new(StringComparer.OrdinalIgnoreCase);
    readonly object _lock = new();

    /// Raised on a thread-pool thread whenever a repository is marked changed.
    public event Action? Changed;

    public void Sync(IReadOnlyCollection<string> repos)
    {
        lock (_lock)
        {
            foreach (var gone in _watchers.Keys.Where(k => !repos.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            {
                _watchers[gone].Dispose();
                _watchers.Remove(gone);
            }
            foreach (var repo in repos.Where(r => !_watchers.ContainsKey(r)))
            {
                try
                {
                    var w = new FileSystemWatcher(repo)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                        InternalBufferSize = 64 * 1024,
                    };
                    FileSystemEventHandler on = (_, e) => Mark(repo, e.FullPath);
                    w.Changed += on;
                    w.Created += on;
                    w.Deleted += on;
                    w.Renamed += (_, e) => Mark(repo, e.FullPath);
                    w.Error += (_, _) => Mark(repo, null); // buffer overflow: just re-read the repo
                    w.EnableRaisingEvents = true;
                    _watchers[repo] = w;
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
            }
        }
    }

    void Mark(string repo, string? path)
    {
        if (path != null && Ignored(Path.GetRelativePath(repo, path))) return;
        bool first;
        lock (_lock) first = _dirty.Add(repo);
        if (first) Changed?.Invoke(); // once per repo until it is read: a build's thousand writes raise one event
    }

    /// Churn that never changes what Laneboard shows.
    public static bool Ignored(string rel)
    {
        var r = rel.Replace('/', '\\');
        // The .git folder's own timestamp moves with every write inside it; HEAD and refs raise their own events.
        if (r.Equals(".git", StringComparison.OrdinalIgnoreCase)) return true;
        if (r.StartsWith(@".git\", StringComparison.OrdinalIgnoreCase))
            // .git\index is rewritten by every editor's background `git status`; HEAD and refs are what matter.
            return r.StartsWith(@".git\objects", StringComparison.OrdinalIgnoreCase)
                || r.StartsWith(@".git\logs", StringComparison.OrdinalIgnoreCase)
                || r.Equals(@".git\index", StringComparison.OrdinalIgnoreCase)
                || r.EndsWith(".lock", StringComparison.OrdinalIgnoreCase);
        foreach (var part in r.Split('\\'))
            if (part is "node_modules" or "bin" or "obj" or "Intermediate" or "Saved" or "DerivedDataCache" or ".vs")
                return true;
        return false;
    }

    public List<string> TakeDirty()
    {
        lock (_lock)
        {
            var list = _dirty.ToList();
            _dirty.Clear();
            return list;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var w in _watchers.Values) w.Dispose();
            _watchers.Clear();
        }
    }
}
