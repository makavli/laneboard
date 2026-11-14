namespace Laneboard;

/// Turns what was read from disk and GitHub into what the window shows. Pure, so it is unit-tested.
public sealed record Board(
    IReadOnlyList<ProjectView> Projects, string Summary, string TrayText,
    IReadOnlyList<string> Warnings, string? RunnerChip, string? MinutesChip, bool NeedsUserScope, bool Badge = false)
{
    public static Board Build(IReadOnlyList<LocalInfo> locals, IReadOnlyDictionary<string, RemoteInfo> remote,
        AccountInfo? account, bool localRunner, DateTimeOffset now, bool canCode, IEnumerable<string>? pinned = null)
    {
        var pins = new HashSet<string>(pinned ?? [], StringComparer.OrdinalIgnoreCase);
        var views = new List<ProjectView>();
        int prTotal = 0;
        foreach (var l in locals)
        {
            RemoteInfo? r = l.Slug != null && remote.TryGetValue(l.Slug, out var x) ? x : null;

            var warn = new List<string>();
            if (l.Error != null) warn.Add(l.Error);
            if (r is { Visibility: "PUBLIC", RunnersTotal: > 0 })
                warn.Add("Public repository with a self-hosted runner: a pull request from a fork could run code on that machine.");

            var parts = new List<string> { l.Git.Changed == 0 ? "clean" : $"{l.Git.Changed} changed" };
            if (l.Git.HasUpstream)
            {
                if (l.Git.Ahead > 0) parts.Add($"{l.Git.Ahead} to push");
                if (l.Git.Behind > 0) parts.Add($"{l.Git.Behind} to pull");
            }
            else if (l.Git.Branch != "detached" && l.Error == null) parts.Add("branch not on a remote");
            if (l.LastCommit is { } t) parts.Add($"last commit {Parse.Ago(now - t)}");

            var prs = r?.Prs ?? [];
            int more = (r?.PrTotal ?? 0) - prs.Count;
            prTotal += Math.Max(r?.PrTotal ?? 0, prs.Count);
            var touched = new[] { l.LastCommit, l.HandoffTime }.Max() ?? DateTimeOffset.MinValue;

            views.Add(new ProjectView(
                l.Name, l.Path, l.Git.Branch, r?.Visibility == "PUBLIC",
                string.Join(" · ", parts), l.LastSubject,
                warn, prs, more > 0 ? $"+{more} more open" : null,
                l.Handoff?.Snippet is { Length: > 0 } s ? s : null,
                l.HandoffPath == null ? null : $"{System.IO.Path.GetFileName(l.HandoffPath)}{(l.HandoffTime is { } ht ? " · " + Parse.Ago(now - ht) : "")}",
                l.Handoff?.SafePoint ?? false, l.HandoffPath, l.Slug, touched, l.Git.Changed, canCode,
                r?.RunnersOnline ?? 0, r?.RunnersTotal ?? 0, pins.Contains(l.Path)));
        }

        var sorted = views
            .OrderByDescending(v => v.HasWarnings)
            .ThenByDescending(v => v.HasFailing)
            .ThenByDescending(v => v.SafePoint)
            .ThenByDescending(v => v.Pinned)
            .ThenByDescending(v => v.LastTouched)
            .ToList();

        int n = sorted.Count, changed = sorted.Count(v => v.Changed > 0);
        int prCount = prTotal;
        int failing = sorted.Sum(v => v.Prs.Count(p => p.State == "fail"));
        string summary = $"{n} project{S(n)} · {changed} with changes · {prCount} open pull request{S(prCount)}";

        var tray = App.Name;
        if (failing > 0) tray += $" · {failing} failing check{S(failing)}";
        if (changed > 0) tray += $" · {changed} with changes";

        var global = new List<string>();
        if (account is { GhFound: false })
            global.Add("GitHub CLI not found: pull requests and checks are off. Install it from cli.github.com.");
        else if (account is { SignedIn: false })
            global.Add("GitHub CLI is not signed in: run gh auth login, then refresh.");
        if (account?.Net is > 0)
            global.Add($"GitHub Actions is billing you ${account.Net:0.00} this month.");

        int rTotal = remote.Values.Sum(v => v.RunnersTotal), rOnline = remote.Values.Sum(v => v.RunnersOnline);
        string? runner = rTotal > 0 ? $"Runners {rOnline}/{rTotal} online" : localRunner ? "A runner is running on this PC" : null;
        string? minutes = account?.Minutes is { } m ? $"Actions {m:0} min this month" : null;

        bool badge = sorted.Any(v => v.Problem) || account?.Net is > 0;
        return new Board(sorted, summary, tray, global, runner, minutes, account?.NeedsUserScope ?? false, badge);
    }

    static string S(int n) => n == 1 ? "" : "s";
}
