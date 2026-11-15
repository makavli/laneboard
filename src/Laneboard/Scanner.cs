using System.IO;

namespace Laneboard;

/// Finds Git repositories under the chosen folders and reads their state. Read-only: never writes to a repo.
public static class Scanner
{
    public static List<string> Discover(IEnumerable<string> roots, IEnumerable<string> hidden)
    {
        var skip = new HashSet<string>(hidden, StringComparer.OrdinalIgnoreCase);
        var found = new List<string>();

        void Walk(string dir, int depth)
        {
            if (IsRepo(dir)) { found.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir))); return; }
            if (depth >= 2) return;
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(dir).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
            foreach (var s in subs)
            {
                var name = Path.GetFileName(s);
                if (name.StartsWith('.') || name is "node_modules" or "bin" or "obj") continue;
                try { if (new DirectoryInfo(s).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                Walk(s, depth + 1);
            }
        }

        foreach (var r in roots)
            if (Directory.Exists(r)) Walk(r, 0);
        return found.Distinct(StringComparer.OrdinalIgnoreCase).Where(p => !skip.Contains(p)).ToList();
    }

    // A folder the user didn't clone (an unpacked archive) brings its own .git/config: never let it run a command.
    // --no-optional-locks: never take index.lock, so we cannot collide with the user's own git commands.
    // log.showSignature: a planted gpg.program would run on `git log`. Filters are checked in Read.
    static readonly string[] SafeGit = ["-c", "core.fsmonitor=false", "-c", "log.showSignature=false", "--no-optional-locks"];

    static bool IsRepo(string d) => Directory.Exists(Path.Combine(d, ".git")) || File.Exists(Path.Combine(d, ".git"));

    public static async Task<List<LocalInfo>> ReadAll(IReadOnlyList<string> paths)
    {
        using var gate = new SemaphoreSlim(6);
        var tasks = paths.Select(async p =>
        {
            await gate.WaitAsync();
            try { return await Task.Run(() => Read(p)); }
            finally { gate.Release(); }
        });
        return (await Task.WhenAll(tasks)).ToList();
    }

    public static async Task<LocalInfo> Read(string path)
    {
        var name = Path.GetFileName(path);
        if (!Directory.Exists(path))
            return new(path, name, new("?", 0, 0, 0, false), null, null, null, null, null, null, "Folder not found: it was moved or deleted.");
        var (handoff, handoffPath, handoffTime) = ReadHandoff(path);
        // git status runs a filter's clean command on every file it re-reads; a planted one would run as the user.
        var cfg = await Proc.Run("git", path, [.. SafeGit, "config", "--show-scope", "-z", "--get-regexp", @"^(filter\..*\.(clean|process)|lfs\.extension\..*)$"]);
        // 1 = no match; -1 = no git, said below. Anything else (a timeout, a broken config) must not skip the check.
        var refused = cfg.Code is not (0 or 1 or -1) ? "git could not list its config"
            : Parse.PlantedCommand(cfg.Out) is { } key ? $"its own git config adds a command ({key})" : null;
        if (refused != null)
            return new(path, name, new("?", 0, 0, 0, false), null, null, null, handoff, handoffPath, handoffTime, $"Not read: {refused}.");
        // dirty: never step into submodules, whose own config is not checked.
        var st = await Proc.Run("git", path, [.. SafeGit, "status", "--porcelain=v2", "--branch", "--ignore-submodules=dirty"]);
        if (st.Code == -1)
            return new(path, name, new("?", 0, 0, 0, false), null, null, null, handoff, handoffPath, handoffTime, "Git not found: install Git for Windows.");
        if (st.Code != 0)
        {
            var why = st.Err.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "git status failed";
            return new(path, name, new("?", 0, 0, 0, false), null, null, null, handoff, handoffPath, handoffTime, why.Replace("fatal: ", "Git: "));
        }
        var git = Parse.PorcelainV2(st.Out);

        var log = await Proc.Run("git", path, [.. SafeGit, "log", "-1", "--format=%ct%x1f%s"]);
        DateTimeOffset? last = null; string? subject = null;
        if (log.Code == 0 && log.Out.Split('\x1f') is [var ct, var s] && long.TryParse(ct.Trim(), out var unix))
        {
            last = DateTimeOffset.FromUnixTimeSeconds(unix);
            subject = s.Trim();
        }

        var remote = await Proc.Run("git", path, [.. SafeGit, "remote", "get-url", "origin"]);
        var slug = remote.Code == 0 ? Parse.GitHubSlug(remote.Out) : null;

        return new(path, name, git, last, subject, slug, handoff, handoffPath, handoffTime);
    }

    static (Parse.Handoff?, string?, DateTimeOffset?) ReadHandoff(string repo)
    {
        try
        {
            var file = Path.Combine(repo, "HANDOFF.md");
            if (!File.Exists(file))
                file = Directory.EnumerateFiles(repo, "HANDOFF*.md").OrderBy(f => f.Length).FirstOrDefault() ?? "";
            if (file.Length == 0) return (null, null, null);
            var info = new FileInfo(file);
            if (info.Length > 2_000_000) return (null, file, info.LastWriteTimeUtc); // not a note, don't read it
            return (Parse.HandoffSnippet(File.ReadAllText(file)), file, info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (null, null, null); }
    }

    /// A self-hosted runner (GitHub, Azure DevOps) running on this PC right now.
    public static bool LocalRunner()
    {
        foreach (var n in new[] { "Runner.Listener", "Agent.Listener", "gitlab-runner" })
        {
            var ps = System.Diagnostics.Process.GetProcessesByName(n);
            bool any = ps.Length > 0;
            foreach (var p in ps) p.Dispose();
            if (any) return true;
        }
        return false;
    }
}
